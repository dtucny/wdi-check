using System.Text;
using WdiCheck.Core.Matching;
using WdiCheck.Core.Reporting;
using WdiCheck.Core.WindowsUpdate;

namespace WdiCheck.Core.Tests;

public class WindowsUpdateTests
{
    // Shape of the search script's output (see WindowsUpdateDrivers.Script).
    private const string TwoOffers = """
        {"resultCode":2,"updates":[
          {"title":"Realtek Semiconductor Corp. - Net - 10.80.407.2026","driverDate":"2026-04-07",
           "hardwareId":"PCI\\VEN_10EC&DEV_8125&SUBSYS_7D731462","driverClass":"Net","provider":"Realtek","manufacturer":"Realtek",
           "model":"Realtek Gaming 2.5GbE Family Controller","browseOnly":true,"isHidden":false,"updateId":"a1b2"},
          {"title":"Advanced Micro Devices, Inc. - System - 5.44.0.0","driverDate":"2026-03-16",
           "hardwareId":"PCI\\VEN_1022&DEV_1649","driverClass":"System","provider":"AMD","manufacturer":"AMD",
           "model":"AMD PSP 11.0 Device","browseOnly":false,"isHidden":false,"updateId":"c3d4"}]}
        """;

    [Fact]
    public void ParsesOffers()
    {
        var (offers, error) = WindowsUpdateDrivers.Parse(TwoOffers);

        Assert.Null(error);
        Assert.Equal(2, offers.Count);
        Assert.Equal("10.80.407.2026", offers[0].Version?.Raw);
        Assert.Equal(new DateOnly(2026, 4, 7), offers[0].DriverDate);
        Assert.True(offers[0].BrowseOnly);
        Assert.Equal("5.44.0.0", offers[1].Version?.Raw);
    }

    [Fact]
    public void NoOffersIsSuccess()
    {
        var (offers, error) = WindowsUpdateDrivers.Parse("""{"resultCode":2,"updates":[]}""");
        Assert.Null(error);
        Assert.Empty(offers);
    }

    [Fact]
    public void ScriptErrorIsReported()
    {
        var (offers, error) = WindowsUpdateDrivers.Parse("""{"error":"The service cannot be started (0x80070422)"}""");
        Assert.Empty(offers);
        Assert.Contains("0x80070422", error);
    }

    [Fact]
    public void GarbageOutputIsAnError()
    {
        var (_, error) = WindowsUpdateDrivers.Parse("New-Object : Cannot create COM object");
        Assert.StartsWith("Unexpected output", error);
    }

    [Theory]
    [InlineData("Realtek Semiconductor Corp. - Net - 10.80.407.2026", "10.80.407.2026")]
    [InlineData("Intel - net - 23.60.0.10", "23.60.0.10")]
    [InlineData("MediaTek, Inc. - Bluetooth - 1.1147.0.616", "1.1147.0.616")]
    [InlineData("NVIDIA Corporation Display driver update 32.0.15.9186", "32.0.15.9186")]
    [InlineData("Some driver without a version", null)]
    public void VersionFromTitle(string title, string? expected) =>
        Assert.Equal(expected, WindowsUpdateDrivers.VersionFromTitle(title)?.Raw);

    [Fact]
    public void OfferMatchesDeviceByHardwareOrCompatibleId()
    {
        var (offers, _) = WindowsUpdateDrivers.Parse(TwoOffers);
        var nic = Fixtures.Rtl8125();
        var psp = Fixtures.Device("AMD PSP 11.0 Device", "5.43.0.0", "AMD", "oem102.inf",
            [@"PCI\VEN_1022&DEV_1649&SUBSYS_7D731462&REV_00"], [@"PCI\VEN_1022&DEV_1649&REV_00", @"pci\ven_1022&dev_1649"]);

        Assert.True(offers[0].AppliesTo(nic));   // hardware ID, exact
        Assert.False(offers[1].AppliesTo(nic));
        Assert.True(offers[1].AppliesTo(psp));   // compatible ID, case-insensitive
    }

    [Fact]
    public async Task ExportIncludesTheOffer()
    {
        var (offers, _) = WindowsUpdateDrivers.Parse(TwoOffers);
        var result = new DeviceMatcher(Fixtures.Index()).Match(Fixtures.Rtl8125());
        using var ms = new MemoryStream();

        await ReportExporter.WriteCsvAsync(ms, [result], TestContext.Current.CancellationToken, offers);
        var lines = Encoding.UTF8.GetString(ms.ToArray()).TrimStart((char)0xFEFF).Split(Environment.NewLine);

        Assert.EndsWith("windows_update_offer,windows_update_date", lines[0]);
        Assert.EndsWith("10.80.407.2026,2026-04-07", lines[1]);
    }
}
