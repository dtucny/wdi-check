using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WdiCheck.Core.Updates;

/// <summary>A newer WdiCheck release, with the download that fits this build (null when none does).</summary>
public sealed record AvailableUpdate(Version Version, Uri ReleasePage, Uri? Download);

/// <summary>
/// Asks GitHub for the latest WdiCheck release, at most once a day: the answer and its ETag are saved, so most
/// checks send nothing and the rest are conditional. Failures are quiet; the last saved answer stands.
/// </summary>
public sealed class UpdateChecker
{
    public static readonly Uri DefaultLatestUri = new("https://api.github.com/repos/dtucny/wdi-check/releases/latest");

    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly HttpClient _http;
    private readonly string _statePath;
    private readonly Version _current;
    private readonly Uri _latestUri;
    private readonly Func<DateTimeOffset> _now;

    public UpdateChecker(HttpClient http, string dataDirectory, Version current, Uri? latestUri = null, Func<DateTimeOffset>? now = null)
    {
        _http = http;
        _statePath = Path.Combine(dataDirectory, "update-check.json");
        _current = current;
        _latestUri = latestUri ?? DefaultLatestUri;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The newer release, or null when this is the latest (or nothing is known yet). <paramref name="force"/> asks
    /// GitHub even when it was asked within the day.
    /// </summary>
    public async Task<AvailableUpdate?> CheckAsync(bool packaged, Architecture architecture, bool force, CancellationToken ct)
    {
        var state = LoadState();
        if (force || state is null || _now() - state.Checked >= Interval)
            state = await FetchAsync(state, ct).ConfigureAwait(false) ?? state;
        return state is null ? null : Newer(state.Release, packaged, architecture);
    }

    private async Task<State?> FetchAsync(State? previous, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _latestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            if (previous?.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var tag))
                request.Headers.IfNoneMatch.Add(tag);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            State state;
            if (response.StatusCode == HttpStatusCode.NotModified && previous is not null)
                state = previous with { Checked = _now() };
            else if (response.IsSuccessStatusCode)
            {
                var release = await response.Content.ReadFromJsonAsync(UpdateJsonContext.Default.Release, ct).ConfigureAwait(false);
                if (release is null)
                    return null;
                state = new State { Checked = _now(), ETag = response.Headers.ETag?.ToString(), Release = release };
            }
            else
                return null;

            SaveState(state);
            return state;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException
                                   && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private AvailableUpdate? Newer(Release release, bool packaged, Architecture architecture)
    {
        if (release.Draft || release.Prerelease || ParseTag(release.TagName) is not { } version || version <= _current
            || !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var page))
            return null;
        var asset = AssetName(version, packaged, architecture) is { } name
            ? release.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
            : null;
        return new AvailableUpdate(version, page,
            asset is not null && Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var download) ? download : null);
    }

    /// <summary>"v0.5.0" (or "0.5.0") as 0.5.0; null for anything else.</summary>
    internal static Version? ParseTag(string? tag) =>
        tag is { Length: > 0 } && Version.TryParse(tag.StartsWith('v') ? tag[1..] : tag, out var v) && v.Build >= 0
            ? new Version(v.Major, v.Minor, v.Build)
            : null;

    /// <summary>The release asset for this build, as release.yml names them; null for an architecture it doesn't build.</summary>
    internal static string? AssetName(Version version, bool packaged, Architecture architecture)
    {
        var arch = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };
        return arch is null ? null : $"WdiCheck-{version.ToString(3)}-win-{arch}{(packaged ? "-unsigned.msix" : ".zip")}";
    }

    private State? LoadState()
    {
        try
        {
            return File.Exists(_statePath) ? JsonSerializer.Deserialize(File.ReadAllText(_statePath), UpdateJsonContext.Default.State) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void SaveState(State state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, JsonSerializer.Serialize(state, UpdateJsonContext.Default.State));
    }

    internal sealed record State
    {
        public DateTimeOffset Checked { get; init; }

        public string? ETag { get; init; }

        public Release Release { get; init; } = new();
    }

    /// <summary>The fields used from GitHub's release object.</summary>
    internal sealed record Release
    {
        public string? TagName { get; init; }

        public string? HtmlUrl { get; init; }

        public bool Draft { get; init; }

        public bool Prerelease { get; init; }

        public List<Asset> Assets { get; init; } = [];
    }

    internal sealed record Asset
    {
        public string? Name { get; init; }

        public string? BrowserDownloadUrl { get; init; }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(UpdateChecker.State))]
[JsonSerializable(typeof(UpdateChecker.Release))]
internal partial class UpdateJsonContext : JsonSerializerContext
{
}
