using System.Text;
using System.Text.Json;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Matching;
using WdiCheck.Core.Reporting;

namespace WdiCheck.Core.Tests;

public class ReportExporterTests
{
    private static IReadOnlyList<DeviceResult> Results()
    {
        var matcher = new DeviceMatcher(Fixtures.Index());
        return
        [
            matcher.Match(Fixtures.Rtl8125()),
            matcher.Match(Fixtures.Device("=cmd|' /C calc'!A0, \"quoted\"", "1.0", "Evil, Inc.", "oem1.inf", [@"USB\VID_FFFF&PID_0001"])),
        ];
    }

    [Fact]
    public async Task CsvHasHeaderAndEscapesValues()
    {
        using var ms = new MemoryStream();
        await ReportExporter.WriteCsvAsync(ms, Results(), TestContext.Current.CancellationToken);
        var text = Encoding.UTF8.GetString(ms.ToArray()).TrimStart((char)0xFEFF);
        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("device,class,status,", lines[0]);
        Assert.Contains("Realtek 8125 LAN", lines[1]);
        Assert.Contains("Behind", lines[1]);
        // Formula injection neutralised and embedded quotes/commas escaped.
        Assert.StartsWith("\"'=cmd|' /C calc'!A0, \"\"quoted\"\"\"", lines[2]);
        Assert.Contains("\"Evil, Inc.\"", lines[2]);
    }

    [Fact]
    public async Task JsonIsWellFormedWithDeviceRows()
    {
        using var ms = new MemoryStream();
        var identity = new SystemIdentity { BaseBoardProduct = "MPG B650I EDGE WIFI (MS-7D73)", BiosVersion = "1.L2" };
        await ReportExporter.WriteJsonAsync(ms, Results(), identity, board: null, indexGenerated: "2026-09-22T15:22:43+00:00", TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(ms.ToArray());
        var root = doc.RootElement;
        Assert.Equal("2026-09-22T15:22:43+00:00", root.GetProperty("index_generated").GetString());
        Assert.Equal("1.L2", root.GetProperty("system").GetProperty("bios_version").GetString());
        var nic = root.GetProperty("devices")[0];
        Assert.Equal("Behind", nic.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Number, nic.GetProperty("lag_days").ValueKind);
        Assert.StartsWith("https://", nic.GetProperty("download_url").GetString());
    }
}
