using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using WdiCheck.Core.Updates;

namespace WdiCheck.Core.Tests;

public sealed class UpdateCheckerTests : IDisposable
{
    private const string LatestJson =
        """
        {"tag_name":"v0.6.0","html_url":"https://github.com/dtucny/wdi-check/releases/tag/v0.6.0","draft":false,"prerelease":false,
         "assets":[
          {"name":"WdiCheck-0.6.0-win-arm64-unsigned.msix","browser_download_url":"https://github.com/dtucny/wdi-check/releases/download/v0.6.0/WdiCheck-0.6.0-win-arm64-unsigned.msix"},
          {"name":"WdiCheck-0.6.0-win-arm64.zip","browser_download_url":"https://github.com/dtucny/wdi-check/releases/download/v0.6.0/WdiCheck-0.6.0-win-arm64.zip"},
          {"name":"WdiCheck-0.6.0-win-x64-unsigned.msix","browser_download_url":"https://github.com/dtucny/wdi-check/releases/download/v0.6.0/WdiCheck-0.6.0-win-x64-unsigned.msix"},
          {"name":"WdiCheck-0.6.0-win-x64.zip","browser_download_url":"https://github.com/dtucny/wdi-check/releases/download/v0.6.0/WdiCheck-0.6.0-win-x64.zip"}]}
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wdicheck-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _handler = new();
    private DateTimeOffset _now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData(false, Architecture.X64, "WdiCheck-0.6.0-win-x64.zip")]
    [InlineData(true, Architecture.X64, "WdiCheck-0.6.0-win-x64-unsigned.msix")]
    [InlineData(false, Architecture.Arm64, "WdiCheck-0.6.0-win-arm64.zip")]
    [InlineData(true, Architecture.Arm64, "WdiCheck-0.6.0-win-arm64-unsigned.msix")]
    public async Task NewerReleaseOffersTheDownloadForThisBuild(bool packaged, Architecture arch, string asset)
    {
        _handler.Next = () => Ok(LatestJson, "\"r1\"");

        var update = await Checker("0.5.0").CheckAsync(packaged, arch, force: false, TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 6, 0), update.Version);
        Assert.Equal("https://github.com/dtucny/wdi-check/releases/tag/v0.6.0", update.ReleasePage.AbsoluteUri);
        Assert.EndsWith("/" + asset, update.Download!.AbsoluteUri);
        Assert.Equal("application/vnd.github+json", _handler.Requests.Single().Headers.Accept.Single().MediaType);
    }

    [Theory]
    [InlineData("0.6.0")]
    [InlineData("0.7.0")]
    public async Task SameOrOlderReleaseIsNoUpdate(string current)
    {
        _handler.Next = () => Ok(LatestJson, "\"r1\"");
        Assert.Null(await Checker(current).CheckAsync(false, Architecture.X64, false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ArchitectureWithoutABuildStillPointsAtTheRelease()
    {
        _handler.Next = () => Ok(LatestJson, "\"r1\"");

        var update = await Checker("0.5.0").CheckAsync(false, Architecture.X86, false, TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Null(update.Download);
    }

    [Fact]
    public async Task AsksAtMostOnceADayThenRevalidates()
    {
        _handler.Next = () => Ok(LatestJson, "\"r1\"");
        var checker = Checker("0.5.0");
        await checker.CheckAsync(false, Architecture.X64, false, TestContext.Current.CancellationToken);

        _now = _now.AddHours(23);
        Assert.NotNull(await checker.CheckAsync(false, Architecture.X64, false, TestContext.Current.CancellationToken));
        Assert.Single(_handler.Requests);

        _now = _now.AddHours(2);
        _handler.Next = () => new HttpResponseMessage(HttpStatusCode.NotModified);
        Assert.NotNull(await checker.CheckAsync(false, Architecture.X64, false, TestContext.Current.CancellationToken));
        Assert.Equal(2, _handler.Requests.Count);
        Assert.Contains(_handler.Requests[1].Headers.IfNoneMatch, e => e.Tag == "\"r1\"");

        // The 304 counts as a check: the next hour asks nothing.
        _now = _now.AddHours(1);
        await checker.CheckAsync(false, Architecture.X64, false, TestContext.Current.CancellationToken);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task ForceAsksWithinTheDay()
    {
        _handler.Next = () => Ok(LatestJson, "\"r1\"");
        var checker = Checker("0.5.0");
        await checker.CheckAsync(false, Architecture.X64, false, TestContext.Current.CancellationToken);

        await checker.CheckAsync(false, Architecture.X64, force: true, TestContext.Current.CancellationToken);

        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task OfflineKeepsTheLastAnswer()
    {
        _handler.Next = () => Ok(LatestJson, "\"r1\"");
        await Checker("0.5.0").CheckAsync(false, Architecture.X64, false, TestContext.Current.CancellationToken);

        _now = _now.AddDays(2);
        _handler.Next = () => throw new HttpRequestException("No such host is known.");
        var update = await Checker("0.5.0").CheckAsync(false, Architecture.X64, false, TestContext.Current.CancellationToken);

        Assert.Equal(new Version(0, 6, 0), update?.Version);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{\"message\":\"API rate limit exceeded\"}")]
    [InlineData(HttpStatusCode.OK, "{\"tag_name\": [garbled")]
    public async Task FailureWithNothingSavedIsNoUpdate(HttpStatusCode status, string body)
    {
        _handler.Next = () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        Assert.Null(await Checker("0.5.0").CheckAsync(false, Architecture.X64, false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PrereleaseIsNoUpdate()
    {
        _handler.Next = () => Ok(LatestJson.Replace("\"prerelease\":false", "\"prerelease\":true"), "\"r1\"");
        Assert.Null(await Checker("0.5.0").CheckAsync(false, Architecture.X64, false, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("v0.6.0", "0.6.0")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3.4", "1.2.3")]
    [InlineData("v1.2", null)]
    [InlineData("nightly", null)]
    [InlineData(null, null)]
    public void TagsParseAsThreePartVersions(string? tag, string? expected) =>
        Assert.Equal(expected, UpdateChecker.ParseTag(tag)?.ToString());

    private UpdateChecker Checker(string current) => new(new HttpClient(_handler), _dir, Version.Parse(current), now: () => _now);

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
