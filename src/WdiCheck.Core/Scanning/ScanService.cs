using WdiCheck.Core.Api;
using WdiCheck.Core.Index;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Matching;

namespace WdiCheck.Core.Scanning;

public sealed record DataFreshness(string? IndexGenerated, DateTimeOffset FetchedAt, bool IsStale, string? Error, bool UsedNetwork);

public sealed record ScanSnapshot
{
    public required DriverIndex Index { get; init; }
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
}

/// <summary>
/// One scan: fetch (or revalidate) the index, enumerate local devices, match them,
/// identify the machine and pull its by-board report.
/// </summary>
public sealed class ScanService(WdiClient client, IDeviceInventory inventory, Func<SystemIdentity>? identityReader = null)
{
    private readonly Func<SystemIdentity> _identityReader = identityReader ?? SystemIdentity.ReadFromRegistry;

    public WdiClient Client => client;

    public async Task<ScanSnapshot> RunAsync(bool revalidate, int? boardOverrideId, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Downloading WinDriverIndex data…");
        var familiesTask = client.GetFamiliesAsync(revalidate, ct);
        var waterTask = client.GetWaterLevelAsync(revalidate, ct);
        var artefactsTask = client.GetArtefactsAsync(revalidate, ct);
        var boardsTask = client.GetBoardsAsync(revalidate, ct);

        // Device enumeration doesn't depend on the index; overlap it with the downloads.
        var devicesTask = inventory.GetDevicesAsync(ct);
        var identity = await Task.Run(_identityReader, ct).ConfigureAwait(false);

        await Task.WhenAll(familiesTask, waterTask, artefactsTask, boardsTask).ConfigureAwait(false);
        var families = familiesTask.Result;
        var water = waterTask.Result;
        var artefacts = artefactsTask.Result;
        var boards = boardsTask.Result;

        progress?.Report("Building local index…");
        var index = await Task.Run(() => DriverIndex.Build(families.Value.Data, water.Value.Data, artefacts.Value.Data, families.Value.Generated), ct)
            .ConfigureAwait(false);

        progress?.Report("Reading installed drivers…");
        var devices = await devicesTask.ConfigureAwait(false);

        progress?.Report("Matching devices…");
        var results = await Task.Run(() => new DeviceMatcher(index).MatchAll(devices), ct).ConfigureAwait(false);

        progress?.Report("Identifying this machine…");
        var candidates = BoardMatcher.Rank(identity, boards.Value.Data);

        var fetches = new[] { families.Source, water.Source, artefacts.Source, boards.Source };
        var freshness = new DataFreshness(
            families.Value.Generated,
            new[] { families.FetchedAt, water.FetchedAt, artefacts.FetchedAt, boards.FetchedAt }.Min(),
            fetches.Contains(FetchSource.StaleCache),
            families.Error ?? water.Error ?? artefacts.Error ?? boards.Error,
            fetches.Any(s => s is FetchSource.Network or FetchSource.Revalidated));

        var snapshot = new ScanSnapshot
        {
            Index = index,
            Boards = boards.Value.Data,
            Results = results,
            Identity = identity,
            BoardCandidates = candidates,
            Freshness = freshness,
        };

        var overrideBoard = boardOverrideId is { } oid ? boards.Value.Data.FirstOrDefault(b => b.BoardId == oid) : null;
        var selected = overrideBoard ?? BoardMatcher.AutoSelect(candidates)?.Board;
        if (selected is null)
            return snapshot;

        progress?.Report($"Loading report for {selected.Name}…");
        return await WithBoardAsync(snapshot, selected, overrideBoard is not null, revalidate, ct).ConfigureAwait(false);
    }

    /// <summary>Re-evaluate an existing scan against a different board (user override).</summary>
    public async Task<ScanSnapshot> WithBoardAsync(ScanSnapshot snapshot, Board? board, bool isOverride, bool revalidate, CancellationToken ct)
    {
        if (board is null)
            return snapshot with { SelectedBoard = null, Board = null, BoardError = null, BoardIsOverride = isOverride };

        try
        {
            var report = await client.GetBoardReportAsync(board.BoardId, revalidate, ct).ConfigureAwait(false);
            return snapshot with
            {
                SelectedBoard = board,
                BoardIsOverride = isOverride,
                Board = BoardAssessment.Create(report.Value, snapshot.Identity, snapshot.Results),
                BoardError = null,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or WdiNotFoundException or System.Text.Json.JsonException or IOException)
        {
            return snapshot with { SelectedBoard = board, BoardIsOverride = isOverride, Board = null, BoardError = ex.Message };
        }
    }
}
