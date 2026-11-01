using System.Net;
using System.Text;
using WdiCheck.Core.Api;

namespace WdiCheck.Core.Tests;

public sealed class WdiClientTests : IDisposable
{
    private const string FamiliesJson =
        """{"schema_version":"1.0.0","license":"CC-BY-4.0","generated":"2026-09-22T15:22:43+00:00","data":[{"family_id":88,"name":"Realtek 8125 LAN","silicon_vendor":"realtek","component":"lan","hwids":["PCI\\VEN_10EC&DEV_8125"]}]}""";

    private readonly string _cache = Path.Combine(Path.GetTempPath(), "wdicheck-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _handler = new();

    public void Dispose()
    {
        if (Directory.Exists(_cache))
            Directory.Delete(_cache, recursive: true);
    }

    [Theory]
    [InlineData("https://windriverindex.tucny.com/v1/latest/", 1682, "https://windriverindex.tucny.com/board.html#1682")]
    [InlineData("https://mirror.example/wdi/v1/latest", 3878, "https://mirror.example/wdi/board.html#3878")]
    public void BoardPageIsTheSitesPageBesideTheData(string source, int boardId, string expected) =>
        Assert.Equal(expected, WdiClient.BoardPage(new Uri(source), boardId).AbsoluteUri);

    [Fact]
    public void BoardSearchIsTheSitesBoardPageWithoutAnId() =>
        Assert.Equal("https://windriverindex.tucny.com/board.html", WdiClient.BoardSearch(WdiClient.DefaultBaseUri).AbsoluteUri);

    [Fact]
    public async Task DownloadsParsesAndCaches()
    {
        _handler.Next = () => Ok(FamiliesJson, "\"v1\"");
        var client = NewClient();

        var r = await client.GetFamiliesAsync(revalidate: true, TestContext.Current.CancellationToken);

        Assert.Equal(FetchSource.Network, r.Source);
        Assert.Equal("Realtek 8125 LAN", r.Value.Data.Single().Name);
        Assert.Equal(@"PCI\VEN_10EC&DEV_8125", r.Value.Data.Single().Hwids.Single());
        Assert.Equal("https://windriverindex.tucny.com/v1/latest/families.json", _handler.Requests.Single().RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task RevalidatesWithETagAndServesCacheOn304()
    {
        _handler.Next = () => Ok(FamiliesJson, "\"v1\"");
        var client = NewClient();
        await client.GetFamiliesAsync(true, TestContext.Current.CancellationToken);

        _handler.Next = () => new HttpResponseMessage(HttpStatusCode.NotModified);
        var r = await client.GetFamiliesAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal(FetchSource.Revalidated, r.Source);
        Assert.Equal("Realtek 8125 LAN", r.Value.Data.Single().Name);
        Assert.Contains(_handler.Requests[1].Headers.IfNoneMatch, e => e.Tag == "\"v1\"");
    }

    [Fact]
    public async Task FreshCacheSkipsTheNetwork()
    {
        _handler.Next = () => Ok(FamiliesJson, "\"v1\"");
        var client = NewClient();
        await client.GetFamiliesAsync(true, TestContext.Current.CancellationToken);

        var r = await client.GetFamiliesAsync(revalidate: false, TestContext.Current.CancellationToken);

        Assert.Equal(FetchSource.Cache, r.Source);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task ManifestHashMatchSkipsTheNetworkEvenWhenRevalidating()
    {
        _handler.Next = () => Ok(FamiliesJson, "\"v1\"");
        var client = NewClient();
        await client.GetFamiliesAsync(true, TestContext.Current.CancellationToken);
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(FamiliesJson)));
        var manifest = new Manifest { Files = { ["families.json"] = new ManifestFile { Sha256 = sha.ToUpperInvariant() } } };

        var same = await client.GetFamiliesAsync(revalidate: true, TestContext.Current.CancellationToken, manifest);
        Assert.Equal(FetchSource.Cache, same.Source);
        Assert.Single(_handler.Requests);

        // A changed hash forces a conditional request even inside max-age.
        var changed = new Manifest { Files = { ["families.json"] = new ManifestFile { Sha256 = "00" } } };
        _handler.Next = () => new HttpResponseMessage(HttpStatusCode.NotModified);
        var r = await client.GetFamiliesAsync(revalidate: false, TestContext.Current.CancellationToken, changed);
        Assert.Equal(FetchSource.Revalidated, r.Source);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task OfflineFallsBackToStaleCache()
    {
        _handler.Next = () => Ok(FamiliesJson, "\"v1\"");
        var client = NewClient();
        await client.GetFamiliesAsync(true, TestContext.Current.CancellationToken);

        _handler.Next = () => throw new HttpRequestException("No such host is known.");
        var r = await client.GetFamiliesAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal(FetchSource.StaleCache, r.Source);
        Assert.Equal("No such host is known.", r.Error);
        Assert.Single(r.Value.Data);
    }

    [Fact]
    public async Task OfflineWithoutCacheThrows()
    {
        _handler.Next = () => throw new HttpRequestException("offline");
        await Assert.ThrowsAsync<HttpRequestException>(() => NewClient().GetFamiliesAsync(true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GarbledDownloadDoesNotReplaceGoodCache()
    {
        _handler.Next = () => Ok(FamiliesJson, "\"v1\"");
        var client = NewClient();
        await client.GetFamiliesAsync(true, TestContext.Current.CancellationToken);

        _handler.Next = () => Ok("{\"data\": [truncated", "\"v2\"");
        var r = await client.GetFamiliesAsync(true, TestContext.Current.CancellationToken);

        Assert.Equal(FetchSource.StaleCache, r.Source);
        Assert.Equal("Realtek 8125 LAN", r.Value.Data.Single().Name);
    }

    [Fact]
    public async Task MissingBoardReportIsNotFound()
    {
        _handler.Next = () => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>404</html>") };
        await Assert.ThrowsAsync<WdiNotFoundException>(() => NewClient().GetBoardReportAsync(99999, true, TestContext.Current.CancellationToken));
    }

    private WdiClient NewClient() => new(new HttpClient(_handler), _cache);

    private static HttpResponseMessage Ok(string body, string etag)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return r;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Next { get; set; } = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Next());
        }
    }
}
