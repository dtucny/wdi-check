using System.Text;
using System.Text.Json;
using WdiCheck.Core.Inventory;
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

        Assert.EndsWith("windows_update_offer,windows_update_date,windows_update_note", lines[0]);
        Assert.EndsWith("10.80.407.2026,2026-04-07,optional", lines[1]);
    }

    // What Windows Update offered WILLOW (Lenovo Legion Slim 5 16APH8, BIOS M3CN50WW built 2026-04-23).
    private const string WillowOffers = """
        {"resultCode":2,"updates":[
          {"title":"Lenovo System Driver Update (26.8.0.29)","driverDate":"2026-08-25","hardwareId":"root\\udsudcdriver",
           "driverClass":"OtherHardware","provider":"Lenovo","browseOnly":false,"isHidden":false,"updateId":"u1"},
          {"title":"Lenovo Ltd. Firmware Driver Update (1.50.0.0)","driverDate":"2026-04-23",
           "hardwareId":"uefi\\res_{26c22f8e-d4d3-4aa8-86bb-53eec694a356}","driverClass":"Firmware","provider":"Lenovo Ltd.",
           "browseOnly":false,"isHidden":false,"updateId":"u2"}]}
        """;

    private static readonly Guid WillowBiosResource = Guid.Parse("26c22f8e-d4d3-4aa8-86bb-53eec694a356");

    private static SystemIdentity Willow(DateOnly? biosDate = null, params Guid[] systemFirmware) => new()
    {
        SystemManufacturer = "LENOVO",
        SystemProductName = "82Y9",
        BiosVersion = "M3CN50WW",
        BiosReleaseDate = biosDate ?? new DateOnly(2026, 4, 23),
        SystemFirmwareResources = systemFirmware,
    };

    [Fact]
    public void FirmwarePackageDatedLikeTheBiosMatchesIt()
    {
        var (offers, _) = WindowsUpdateDrivers.Parse(WillowOffers);

        Assert.Equal("1.50.0.0", offers[1].Version?.Raw);
        Assert.Equal(WillowBiosResource, offers[1].FirmwareResource);
        Assert.True(offers[1].MatchesInstalledBios(Willow(systemFirmware: WillowBiosResource)));
        Assert.True(offers[1].MatchesInstalledBios(Willow()));   // no ESRT: class and date decide
        Assert.False(offers[0].MatchesInstalledBios(Willow()));  // not firmware
    }

    [Fact]
    public void NewerOrOtherFirmwareIsAnUpdate()
    {
        var (offers, _) = WindowsUpdateDrivers.Parse(WillowOffers);

        Assert.False(offers[1].MatchesInstalledBios(Willow(new DateOnly(2025, 11, 2))));       // older BIOS installed
        Assert.False(offers[1].MatchesInstalledBios(Willow(systemFirmware: Guid.NewGuid())));  // device firmware, not the BIOS
    }

    [Fact]
    public async Task JsonExportListsWhatEachOfferAppliesTo()
    {
        var (offers, _) = WindowsUpdateDrivers.Parse(WillowOffers);
        var udc = Fixtures.Device("Universal Device Client Device", "26.7.0.7", "Lenovo", "oem182.inf", [@"ROOT\udsudcdriver"], []);
        var firmware = Fixtures.Device("System Firmware", "10.0.26100.4768", "Microsoft", "c_firmware.inf",
            [@"UEFI\RES_{26c22f8e-d4d3-4aa8-86bb-53eec694a356}&REV_32"], [@"UEFI\RES_{26c22f8e-d4d3-4aa8-86bb-53eec694a356}"]);
        var matcher = new DeviceMatcher(Fixtures.Index());
        using var ms = new MemoryStream();

        await ReportExporter.WriteJsonAsync(ms, [matcher.Match(udc), matcher.Match(firmware)], Willow(), null, null,
            TestContext.Current.CancellationToken, offers);
        using var doc = JsonDocument.Parse(ms.ToArray());
        var json = doc.RootElement.GetProperty("windows_update_offers");

        Assert.False(json[0].GetProperty("matches_installed_bios").GetBoolean());
        Assert.Equal("Universal Device Client Device", json[0].GetProperty("applies_to")[0].GetProperty("device").GetString());
        Assert.True(json[1].GetProperty("matches_installed_bios").GetBoolean());
        Assert.Equal("System Firmware", json[1].GetProperty("applies_to")[0].GetProperty("device").GetString());
        var devices = doc.RootElement.GetProperty("devices");
        Assert.Equal("installs automatically", devices[0].GetProperty("windows_update_note").GetString());
        Assert.Equal("matches installed BIOS", devices[1].GetProperty("windows_update_note").GetString());
    }
}
