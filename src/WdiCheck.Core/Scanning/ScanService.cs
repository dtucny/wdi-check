using WdiCheck.Core.Api;
using WdiCheck.Core.Index;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Matching;

namespace WdiCheck.Core.Scanning;

public sealed record DataFreshness(string? IndexGenerated, DateTimeOffset FetchedAt, bool IsStale, string? Error, bool UsedNetwork,
    string? SchemaVersion = null, bool HasInfData = false)
{
    /// <summary>The newest data schema this build understands; a newer major may have breaking changes.</summary>
    public const int SupportedSchemaMajor = 2;

    public bool IsNewerSchema =>
        SchemaVersion is { } v && int.TryParse(v.Split('.')[0], out var major) && major > SupportedSchemaMajor;
}

public sealed record ScanSnapshot
{
    public required DriverIndex Index { get; init; }

    /// <summary>INF-level index; null on mirrors older than schema 2.0.0.</summary>
    public InfIndex? Infs { get; init; }

    public required IReadOnlyList<Board> Boards { get; init; }
    public required IReadOnlyList<DeviceResult> Results { get; init; }
    public required SystemIdentity Identity { get; init; }
    public required IReadOnlyList<BoardCandidate> BoardCandidates { get; init; }
    public required DataFreshness Freshness { get; init; }
    public Board? SelectedBoard { get; init; }

    /// <summary>True when the board came from the user's override rather than automatic matching.</summary>
    public bool BoardIsOverride { get; init; }

    public BoardAssessment? Board { get; init; }
    public string? BoardError { get; init; }

    /// <summary>Add-in graphics cards, each with the catalogue's cards it could be.</summary>
    public IReadOnlyList<CardAssessment> Cards { get; init; } = [];
}

/// <summary>
/// One scan: fetch (or revalidate) the index, enumerate local devices, match them,
/// identify the machine and pull its by-board report.
/// </summary>
public sealed class ScanService(WdiClient client, IDeviceInventory inventory, Func<SystemIdentity>? identityReader = null)
{
    private readonly Func<SystemIdentity> _identityReader = identityReader ?? SystemIdentity.ReadFromRegistry;

    public WdiClient Client => client;

    /// <param name="cardOverrides">The user's choice of model per card, by <see cref="GraphicsCard.Key"/>.</param>
    public async Task<ScanSnapshot> RunAsync(bool revalidate, int? boardOverrideId, IProgress<string>? progress, CancellationToken ct,
        IReadOnlyDictionary<string, int>? cardOverrides = null)
    {
        // Device enumeration doesn't depend on the index; overlap it with the downloads.
        var devicesTask = inventory.GetDevicesAsync(ct);
        var identityTask = Task.Run(_identityReader, ct);

        progress?.Report("Checking for new WinDriverIndex data…");
        // Schema 2.0.0: the manifest's hashes tell which cached files are still current, so an unchanged
        // index costs this one request. Older mirrors have no manifest; files are then revalidated one by one.
        var manifest = (await TryGetAsync(() => client.GetManifestAsync(revalidate, ct)).ConfigureAwait(false))?.Value.Data;

        progress?.Report("Downloading WinDriverIndex data…");
        var familiesTask = client.GetFamiliesAsync(revalidate, ct, manifest);
        var waterTask = client.GetWaterLevelAsync(revalidate, ct, manifest);
        var artefactsTask = client.GetArtefactsAsync(revalidate, ct, manifest);
        var boardsTask = client.GetBoardsAsync(revalidate, ct, manifest);
        var infsTask = TryGetAsync(() => client.GetInfsAsync(revalidate, ct, manifest));

        await Task.WhenAll(familiesTask, waterTask, artefactsTask, boardsTask, infsTask).ConfigureAwait(false);
        var families = familiesTask.Result;
        var water = waterTask.Result;
        var artefacts = artefactsTask.Result;
        var boards = boardsTask.Result;
        var infs = infsTask.Result;

        progress?.Report("Building local index…");
        var (index, infIndex) = await Task.Run(() => (
                DriverIndex.Build(families.Value.Data, water.Value.Data, artefacts.Value.Data, families.Value.Generated),
                infs is null ? null : InfIndex.Build(infs.Value.Data, artefacts.Value.Data)), ct)
            .ConfigureAwait(false);

        progress?.Report("Reading installed drivers…");
        var devices = await devicesTask.ConfigureAwait(false);
        var identity = await identityTask.ConfigureAwait(false);

        progress?.Report("Matching devices…");
        var results = await Task.Run(() => new DeviceMatcher(index, infIndex).MatchAll(devices), ct).ConfigureAwait(false);

        progress?.Report("Identifying this machine…");
        var candidates = BoardMatcher.Rank(identity, boards.Value.Data);

        var fetched = new List<(FetchSource Source, DateTimeOffset At, string? Error)>
        {
            (families.Source, families.FetchedAt, families.Error),
            (water.Source, water.FetchedAt, water.Error),
            (artefacts.Source, artefacts.FetchedAt, artefacts.Error),
            (boards.Source, boards.FetchedAt, boards.Error),
        };
        if (infs is not null)
            fetched.Add((infs.Source, infs.FetchedAt, infs.Error));
        var freshness = new DataFreshness(
            families.Value.Generated,
            fetched.Min(f => f.At),
            fetched.Any(f => f.Source == FetchSource.StaleCache),
            fetched.Select(f => f.Error).FirstOrDefault(e => e is not null),
            fetched.Any(f => f.Source is FetchSource.Network or FetchSource.Revalidated),
            families.Value.SchemaVersion,
            infIndex is { Count: > 0 });

        var snapshot = new ScanSnapshot
        {
            Index = index,
            Infs = infIndex,
            Boards = boards.Value.Data,
            Results = results,
            Identity = identity,
            BoardCandidates = candidates,
            Freshness = freshness,
        };

        var cards = CardMatcher.Find(results, boards.Value.Data);
        if (cards.Count > 0)
        {
            progress?.Report(cards.Count == 1 ? "Loading the graphics card's listing…" : "Loading the graphics cards' listings…");
            var assessed = await Task.WhenAll(cards.Select(c =>
            {
                var chosen = cardOverrides?.GetValueOrDefault(c.Key) is { } id ? boards.Value.Data.FirstOrDefault(b => b.BoardId == id) : null;
                return LoadCardAsync(c, chosen, identity, freshness.IndexGenerated, revalidate, ct);
            })).ConfigureAwait(false);
            snapshot = snapshot with { Cards = assessed };
        }

        var overrideBoard = boardOverrideId is { } oid ? boards.Value.Data.FirstOrDefault(b => b.BoardId == oid) : null;
        var selected = overrideBoard ?? BoardMatcher.AutoSelect(candidates)?.Board;
        if (selected is null)
            return snapshot;

        progress?.Report($"Loading report for {selected.Name}…");
        return await WithBoardAsync(snapshot, selected, overrideBoard is not null, revalidate, ct).ConfigureAwait(false);
    }

    /// <summary>More than this many candidates (a chip's whole range from one maker) isn't worth a report each.</summary>
    internal const int MaxCardReports = 48;

    /// <summary>Re-evaluate one card against the user's choice of model, or automatic matching (null).</summary>
    public async Task<ScanSnapshot> WithCardAsync(ScanSnapshot snapshot, string key, Board? chosen, CancellationToken ct)
    {
        if (snapshot.Cards.FirstOrDefault(c => c.Card.Key == key) is not { } current)
            return snapshot;
        var updated = await LoadCardAsync(current.Card, chosen, snapshot.Identity, snapshot.Freshness.IndexGenerated, revalidate: false, ct).ConfigureAwait(false);
        return snapshot with { Cards = snapshot.Cards.Select(c => c.Card.Key == key ? updated : c).ToList() };
    }

    private async Task<CardAssessment> LoadCardAsync(GraphicsCard card, Board? chosen, SystemIdentity identity, string? indexGenerated,
        bool revalidate, CancellationToken ct)
    {
        var selected = chosen ?? (card.Candidates.Count == 1 ? card.Candidates[0] : null);
        // The chosen model's report, or every candidate's: whether any lists a VBIOS decides whether to ask for the model.
        var wanted = selected is not null ? [selected] : card.Candidates.Take(MaxCardReports).ToList();
        string? error = null;
        using var gate = new SemaphoreSlim(6);
        var reports = await Task.WhenAll(wanted.Select(async b =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var report = await GetReportAsync(b.BoardId, indexGenerated, revalidate, ct).ConfigureAwait(false);
                return BoardAssessment.Create(report, identity, [card.Gpu], assessBios: false);
            }
            catch (Exception ex) when (ex is HttpRequestException or WdiNotFoundException or System.Text.Json.JsonException or IOException)
            {
                error ??= ex.Message;
                return null;
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        return new CardAssessment
        {
            Card = card,
            Selected = selected,
            IsOverride = chosen is not null,
            Reports = reports.OfType<BoardAssessment>().ToList(),
            Error = error,
        };
    }

    private async Task<BoardReport> GetReportAsync(int boardId, string? indexGenerated, bool revalidate, CancellationToken ct)
    {
        var report = await client.GetBoardReportAsync(boardId, revalidate, ct).ConfigureAwait(false);
        // Board reports aren't in the manifest, so a cached one can predate the index it's shown with
        // (e.g. from before a publish that added BIOS versions): ask the server again in that case.
        if (!revalidate && IsOlder(report.Value.Generated, indexGenerated))
            report = await client.GetBoardReportAsync(boardId, revalidate: true, ct).ConfigureAwait(false);
        return report.Value;
    }

    /// <summary>Optional files (added in schema 2.0.0): missing on older mirrors, or unreachable with nothing cached.</summary>
    private static async Task<FetchResult<T>?> TryGetAsync<T>(Func<Task<FetchResult<T>>> fetch) where T : class
    {
        try
        {
            return await fetch().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WdiNotFoundException or HttpRequestException or System.Text.Json.JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>Re-evaluate an existing scan against a different board (user override).</summary>
    public async Task<ScanSnapshot> WithBoardAsync(ScanSnapshot snapshot, Board? board, bool isOverride, bool revalidate, CancellationToken ct)
    {
        if (board is null)
            return snapshot with { SelectedBoard = null, Board = null, BoardError = null, BoardIsOverride = isOverride };

        try
        {
            var report = await GetReportAsync(board.BoardId, snapshot.Freshness.IndexGenerated, revalidate, ct).ConfigureAwait(false);
            return snapshot with
            {
                SelectedBoard = board,
                BoardIsOverride = isOverride,
                Board = BoardAssessment.Create(report, snapshot.Identity, snapshot.Results),
                BoardError = null,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or WdiNotFoundException or System.Text.Json.JsonException or IOException)
        {
            return snapshot with { SelectedBoard = board, BoardIsOverride = isOverride, Board = null, BoardError = ex.Message };
        }
    }

    internal static bool IsOlder(string? generated, string? than) =>
        DateTimeOffset.TryParse(generated, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var a)
        && DateTimeOffset.TryParse(than, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var b)
        && a < b;
}
