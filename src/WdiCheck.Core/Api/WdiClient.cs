using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace WdiCheck.Core.Api;

public enum FetchSource
{
    /// <summary>Fresh body downloaded (HTTP 200).</summary>
    Network,
    /// <summary>Server confirmed the cached copy is current (HTTP 304).</summary>
    Revalidated,
    /// <summary>Cached copy used without contacting the server (within max-age, or its hash matches the manifest).</summary>
    Cache,
    /// <summary>Network failed; an older cached copy was used.</summary>
    StaleCache,
}

public sealed record FetchResult<T>(T Value, FetchSource Source, DateTimeOffset FetchedAt, string? Error);

public sealed class CacheMeta
{
    public string? Url { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>SHA-256 of the cached body, compared with manifest.json to skip unchanged files.</summary>
    public string? Sha256 { get; set; }
}

/// <summary>
/// Client for the WinDriverIndex static API. Every response is cached on disk and
/// revalidated with If-None-Match / If-Modified-Since, so a refresh normally costs
/// a handful of 304s; when offline the last good copy is served and flagged stale.
/// </summary>
public sealed class WdiClient
{
    public static readonly Uri DefaultBaseUri = new("https://windriverindex.tucny.com/v1/latest/");

    /// <summary>The CDN's own max-age for the aggregate files.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(4);

    private readonly HttpClient _http;
    private readonly string _cacheDir;

    public WdiClient(HttpClient http, string cacheRoot, Uri? baseUri = null)
    {
        _http = http;
        BaseUri = EnsureTrailingSlash(baseUri ?? DefaultBaseUri);
        // Separate cache per data source so switching mirrors never mixes data.
        _cacheDir = Path.Combine(cacheRoot, Sanitize(BaseUri.Host + BaseUri.AbsolutePath));
    }

    public Uri BaseUri { get; }

    /// <summary>
    /// The WinDriverIndex site's page for a board or graphics card: <c>board.html#id</c>, two levels above the data
    /// (<c>/v1/latest/</c>). A mirror of the whole site gets its own page.
    /// </summary>
    public static Uri BoardPage(Uri dataBase, int boardId) => new(EnsureTrailingSlash(dataBase), $"../../board.html#{boardId}");

    /// <summary>The site's board page without a board: its search across the catalogue.</summary>
    public static Uri BoardSearch(Uri dataBase) => new(EnsureTrailingSlash(dataBase), "../../board.html");

    public TimeSpan MaxAge { get; init; } = DefaultMaxAge;

    public string CacheDirectory => _cacheDir;

    public static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
        var version = typeof(WdiClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WdiCheck", version));
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://windriverindex.tucny.com/)"));
        return http;
    }

    /// <summary>2.0.0 manifest: file hashes that let unchanged files be served from cache without a request.</summary>
    public Task<FetchResult<Envelope<Manifest>>> GetManifestAsync(bool revalidate, CancellationToken ct) =>
        GetAsync("manifest.json", WdiJsonContext.Default.EnvelopeManifest, revalidate, ct);

    public Task<FetchResult<Envelope<List<Family>>>> GetFamiliesAsync(bool revalidate, CancellationToken ct, Manifest? manifest = null) =>
        GetAsync("families.json", WdiJsonContext.Default.EnvelopeListFamily, revalidate, ct, Sha(manifest, "families.json"));

    public Task<FetchResult<Envelope<List<WaterLevel>>>> GetWaterLevelAsync(bool revalidate, CancellationToken ct, Manifest? manifest = null) =>
        GetAsync("water-level.json", WdiJsonContext.Default.EnvelopeListWaterLevel, revalidate, ct, Sha(manifest, "water-level.json"));

    public Task<FetchResult<Envelope<List<Artefact>>>> GetArtefactsAsync(bool revalidate, CancellationToken ct, Manifest? manifest = null) =>
        GetAsync("artefacts.json", WdiJsonContext.Default.EnvelopeListArtefact, revalidate, ct, Sha(manifest, "artefacts.json"));

    public Task<FetchResult<Envelope<List<Board>>>> GetBoardsAsync(bool revalidate, CancellationToken ct, Manifest? manifest = null) =>
        GetAsync("boards.json", WdiJsonContext.Default.EnvelopeListBoard, revalidate, ct, Sha(manifest, "boards.json"));

    /// <summary>2.0.0 INF-level versions.</summary>
    public Task<FetchResult<Envelope<List<InfRow>>>> GetInfsAsync(bool revalidate, CancellationToken ct, Manifest? manifest = null) =>
        GetAsync("infs.json", WdiJsonContext.Default.EnvelopeListInfRow, revalidate, ct, Sha(manifest, "infs.json"));

    private static string? Sha(Manifest? manifest, string file) =>
        manifest?.Files.GetValueOrDefault(file)?.Sha256;

    public Task<FetchResult<BoardReport>> GetBoardReportAsync(int boardId, bool revalidate, CancellationToken ct) =>
        GetAsync($"by-board/{boardId}.json", WdiJsonContext.Default.BoardReport, revalidate, ct);

    /// <param name="revalidate">
    /// When true, always ask the server (conditional GET) even if the cached copy is younger than <see cref="MaxAge"/>.
    /// </param>
    /// <param name="expectedSha256">
    /// The file's hash from manifest.json; when the cached body has this hash it is current and no request is made.
    /// </param>
    public async Task<FetchResult<T>> GetAsync<T>(string relativePath, JsonTypeInfo<T> typeInfo, bool revalidate, CancellationToken ct,
        string? expectedSha256 = null)
    {
        var url = new Uri(BaseUri, relativePath);
        var bodyPath = Path.Combine(_cacheDir, Sanitize(relativePath));
        var metaPath = bodyPath + ".meta";
        var meta = await ReadMetaAsync(metaPath, ct).ConfigureAwait(false);
        var haveCache = meta is not null && File.Exists(bodyPath) && meta.Url == url.AbsoluteUri;

        if (haveCache && expectedSha256 is not null && string.Equals(meta!.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new(await ReadBodyAsync(bodyPath, typeInfo, ct).ConfigureAwait(false), FetchSource.Cache, meta.FetchedAt, null);
        }

        if (haveCache && !revalidate && expectedSha256 is null && DateTimeOffset.UtcNow - meta!.FetchedAt < MaxAge)
        {
            return new(await ReadBodyAsync(bodyPath, typeInfo, ct).ConfigureAwait(false), FetchSource.Cache, meta.FetchedAt, null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (haveCache)
            {
                if (!string.IsNullOrEmpty(meta!.ETag) && EntityTagHeaderValue.TryParse(meta.ETag, out var etag))
                    request.Headers.IfNoneMatch.Add(etag);
                if (!string.IsNullOrEmpty(meta.LastModified) && DateTimeOffset.TryParse(meta.LastModified, out var lm))
                    request.Headers.IfModifiedSince = lm;
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;

            if (response.StatusCode == HttpStatusCode.NotModified && haveCache)
            {
                meta!.FetchedAt = now;
                meta.Sha256 ??= await HashFileAsync(bodyPath, ct).ConfigureAwait(false);
                await WriteMetaAsync(metaPath, meta, ct).ConfigureAwait(false);
                return new(await ReadBodyAsync(bodyPath, typeInfo, ct).ConfigureAwait(false), FetchSource.Revalidated, now, null);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new WdiNotFoundException(url);

            response.EnsureSuccessStatusCode();

            Directory.CreateDirectory(Path.GetDirectoryName(bodyPath)!);
            var tmp = bodyPath + ".tmp";
            await using (var fs = File.Create(tmp))
            {
                await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            // Parse before committing so a truncated/garbled download never replaces a good cache.
            var value = await ReadBodyAsync(tmp, typeInfo, ct).ConfigureAwait(false);
            File.Move(tmp, bodyPath, overwrite: true);
            await WriteMetaAsync(metaPath, new CacheMeta
            {
                Url = url.AbsoluteUri,
                ETag = response.Headers.ETag?.ToString(),
                LastModified = response.Content.Headers.LastModified?.ToString("R"),
                FetchedAt = now,
                Sha256 = await HashFileAsync(bodyPath, ct).ConfigureAwait(false),
            }, ct).ConfigureAwait(false);
            return new(value, FetchSource.Network, now, null);
        }
        catch (Exception ex) when (haveCache && !ct.IsCancellationRequested && ex is not WdiNotFoundException)
        {
            var value = await ReadBodyAsync(bodyPath, typeInfo, ct).ConfigureAwait(false);
            return new(value, FetchSource.StaleCache, meta!.FetchedAt, ex.Message);
        }
    }

    public long CacheSizeBytes() =>
        Directory.Exists(_cacheDir)
            ? new DirectoryInfo(_cacheDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;

    public void ClearCache()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    private static async Task<T> ReadBodyAsync<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(fs, typeInfo, ct).ConfigureAwait(false)
               ?? throw new InvalidDataException($"Empty JSON document: {path}");
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
    }

    private static async Task<CacheMeta?> ReadMetaAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(fs, WdiJsonContext.Default.CacheMeta, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteMetaAsync(string path, CacheMeta meta, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, meta, WdiJsonContext.Default.CacheMeta, ct).ConfigureAwait(false);
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Trim('/').Select(c => c == '/' || invalid.Contains(c) ? '_' : c).ToArray());
    }
}

public sealed class WdiNotFoundException(Uri url) : Exception($"Not found: {url}")
{
    public Uri Url { get; } = url;
}
