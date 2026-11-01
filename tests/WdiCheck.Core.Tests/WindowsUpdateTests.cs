using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Matching;
using WdiCheck.Core.Reporting;
using WdiCheck.Core.WindowsUpdate;

namespace WdiCheck.Core.Tests;

public class WindowsUpdateTests
{
    private static readonly List<WindowsUpdateOffer> TwoOffers = Offers(
        new("Realtek Semiconductor Corp. - Net - 10.80.407.2026", new DateTime(2026, 4, 7),
            @"PCI\VEN_10EC&DEV_8125&SUBSYS_7D731462", "Net", "Realtek", "Realtek", "Realtek Gaming 2.5GbE Family Controller",
            BrowseOnly: true, IsHidden: false, "a1b2"),
        new("Advanced Micro Devices, Inc. - System - 5.44.0.0", new DateTime(2026, 3, 16),
            @"PCI\VEN_1022&DEV_1649", "System", "AMD", "AMD", "AMD PSP 11.0 Device",
            BrowseOnly: false, IsHidden: false, "c3d4"));

    private static List<WindowsUpdateOffer> Offers(params AgentDriverUpdate[] updates) =>
        updates.Select(WindowsUpdateDrivers.ToOffer).ToList();

    [Fact]
    public void MapsAgentUpdatesToOffers()
    {
        var offers = TwoOffers;

        Assert.Equal(2, offers.Count);
        Assert.Equal("10.80.407.2026", offers[0].Version?.Raw);
        Assert.Equal(new DateOnly(2026, 4, 7), offers[0].DriverDate);
        Assert.True(offers[0].BrowseOnly);
        Assert.Equal("5.44.0.0", offers[1].Version?.Raw);
    }

    [Fact]
    public void MissingFieldsMapToEmptyOrNull()
    {
        var offer = WindowsUpdateDrivers.ToOffer(new AgentDriverUpdate(null, null, null, null, null, null, null, false, false, null));

        Assert.Equal("", offer.Title);
        Assert.Equal("", offer.HardwareId);
        Assert.Null(offer.Version);
        Assert.Null(offer.DriverDate);
    }

    [Theory]
    [InlineData(0x80070422, "The Windows Update service is disabled. (0x80070422)")]
    [InlineData(0x8024402C, "Couldn't reach Windows Update. Check the network connection. (0x8024402C)")]
    [InlineData(0x8024500C, "Policy on this PC blocks searching Windows Update directly. (0x8024500C)")]
    public void KnownErrorsAreExplained(uint hresult, string expected) =>
        Assert.Equal(expected, WindowsUpdateDrivers.Describe(new COMException("Exception from HRESULT", unchecked((int)hresult))));

    [Theory]
    [InlineData("Something broke", "Something broke (0x80240FFF)")]
    [InlineData("Exception from HRESULT: 0x80240FFF", "Exception from HRESULT: 0x80240FFF")]
    public void OtherErrorsKeepTheirCodeOnce(string message, string expected) =>
        Assert.Equal(expected, WindowsUpdateDrivers.Describe(new COMException(message, unchecked((int)0x80240FFF))));

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
        var offers = TwoOffers;
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
        var offers = TwoOffers;
        var result = new DeviceMatcher(Fixtures.Index()).Match(Fixtures.Rtl8125());
        using var ms = new MemoryStream();

        await ReportExporter.WriteCsvAsync(ms, [result], TestContext.Current.CancellationToken, offers);
        var lines = Encoding.UTF8.GetString(ms.ToArray()).TrimStart((char)0xFEFF).Split(Environment.NewLine);

        Assert.EndsWith("windows_update_offer,windows_update_date,windows_update_note", lines[0]);
        Assert.EndsWith("10.80.407.2026,2026-04-07,optional", lines[1]);
    }

    // What Windows Update offered WILLOW (Lenovo Legion Slim 5 16APH8, BIOS M3CN50WW built 2026-04-23).
    private static readonly List<WindowsUpdateOffer> WillowOffers = Offers(
        new("Lenovo System Driver Update (26.8.0.29)", new DateTime(2026, 8, 25), @"root\udsudcdriver",
            "OtherHardware", "Lenovo", null, null, BrowseOnly: false, IsHidden: false, "u1"),
        new("Lenovo Ltd. Firmware Driver Update (1.50.0.0)", new DateTime(2026, 4, 23),
            @"uefi\res_{26c22f8e-d4d3-4aa8-86bb-53eec694a356}", "Firmware", "Lenovo Ltd.", null, null,
            BrowseOnly: false, IsHidden: false, "u2"));

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
        var offers = WillowOffers;

        Assert.Equal("1.50.0.0", offers[1].Version?.Raw);
        Assert.Equal(WillowBiosResource, offers[1].FirmwareResource);
        Assert.True(offers[1].MatchesInstalledBios(Willow(systemFirmware: WillowBiosResource)));
        Assert.True(offers[1].MatchesInstalledBios(Willow()));   // no ESRT: class and date decide
        Assert.False(offers[0].MatchesInstalledBios(Willow()));  // not firmware
    }

    [Fact]
    public void NewerOrOtherFirmwareIsAnUpdate()
    {
        var offers = WillowOffers;

        Assert.False(offers[1].MatchesInstalledBios(Willow(new DateOnly(2025, 11, 2))));       // older BIOS installed
        Assert.False(offers[1].MatchesInstalledBios(Willow(systemFirmware: Guid.NewGuid())));  // device firmware, not the BIOS
    }

    [Fact]
    public async Task JsonExportListsWhatEachOfferAppliesTo()
    {
        var offers = WillowOffers;
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
