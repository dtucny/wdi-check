using WdiCheck.Core.Index;
using WdiCheck.Core.Matching;
using WdiCheck.Core.Reporting;

namespace WdiCheck.Core.Tests;

public class DeviceMatcherTests
{
    private readonly DeviceMatcher _matcher = new(Fixtures.Index());

    [Fact]
    public void SharedHwidResolvesToFamilyListingTheInstalledVersion()
    {
        // PCI\VEN_10EC&DEV_8125 is in Wi-Fi, 8168, 8125 and 8126 LAN; by-hwid on the site returns 8126 (last writer wins).
        var r = _matcher.Match(Fixtures.Rtl8125());

        Assert.Equal("Realtek 8125 LAN", r.Family?.Name);
        Assert.Equal(3, r.OtherCandidates.Count);
        Assert.Equal(MatchConfidence.ExactVersion, r.Confidence);
        Assert.Equal(@"PCI\VEN_10EC&DEV_8125", r.MatchedId);
        Assert.True(r.MatchedOnCompatibleId);
    }

    [Fact]
    public void BehindOnOwnLineWithLagFromListingDates()
    {
        var r = _matcher.Match(Fixtures.Rtl8125());

        Assert.Equal(DriverStatus.Behind, r.Status);
        Assert.Equal("10.080.0525.2026", r.LineNewest?.Version.Raw);
        // Installed version first listed 2025-12-10; newest on the 10.x line first listed 2026-09-16.
        Assert.Equal(new DateOnly(2026, 9, 16).DayNumber - new DateOnly(2025, 12, 10).DayNumber, r.LagDays);
        Assert.Equal("10.080.0525.2026", r.Target?.Raw);
        Assert.Equal(r.LineNewest?.Version, r.Update?.Version);
        // The family water level (1125.x, Feb 2026) predates the 10.x line's newest, so no parallel-line note.
        Assert.Null(r.ParallelLine);
    }

    [Fact]
    public void DownloadLinksPreferTheBoardVendor()
    {
        var r = _matcher.Match(Fixtures.Rtl8125());
        var links = DownloadLinks.For(r.Target, boardVendor: "msi");

        Assert.Equal(2, links.Count);
        Assert.Equal("msi", links[0].Vendor);
        Assert.True(links[0].IsBoardVendor);
        Assert.All(links, l => Assert.Equal("https", l.Url.Scheme));
    }

    [Fact]
    public void CurrentWhenAtNewestOnLine()
    {
        var r = _matcher.Match(Fixtures.Rtl8125("10.080.0525.2026"));

        Assert.Equal(DriverStatus.Current, r.Status);
        Assert.Equal(0, r.LagDays);
        Assert.False(r.IsNewerThanIndex);
        Assert.Null(r.Target);
    }

    [Fact]
    public void NewerThanIndexIsCurrent()
    {
        var r = _matcher.Match(Fixtures.Rtl8125("10.81.0.1"));

        Assert.Equal(DriverStatus.Current, r.Status);
        Assert.True(r.IsNewerThanIndex);
        Assert.Equal(MatchConfidence.SameLine, r.Confidence);
    }

    [Fact]
    public void ClassCodeCompatibleIdDoesNotMatchInboxNvme()
    {
        // Standard NVMe controller: the only index hit would be PCI\CC_010802 ("AMD RAID").
        var nvme = Fixtures.Device("Standard NVM Express Controller", "10.0.26100.9278", "Microsoft", "stornvme.inf",
            [@"PCI\VEN_144D&DEV_A80A&SUBSYS_A801144D&REV_00", @"PCI\VEN_144D&DEV_A80A&SUBSYS_A801144D"],
            [@"PCI\VEN_144D&DEV_A80A&REV_00", @"PCI\VEN_144D&DEV_A80A", @"PCI\VEN_144D&CC_010802", @"PCI\CC_010802"]);

        var r = _matcher.Match(nvme);

        Assert.Equal(DriverStatus.NotIndexed, r.Status);
        Assert.Null(r.Family);
    }

    [Fact]
    public void InboxDriverOnIndexedHardwareGetsNoVerdict()
    {
        var hda = Fixtures.Device("High Definition Audio Controller", "10.0.26100.9444", "Microsoft", "hdaudbus.inf",
            [@"PCI\VEN_1022&DEV_15E3&SUBSYS_D6011462&REV_00"], [@"PCI\VEN_1022&DEV_15E3"]);

        var r = _matcher.Match(hda);

        Assert.Equal(DriverStatus.Inbox, r.Status);
        Assert.Equal("AMD Graphics", r.Family?.Name);
        Assert.Null(r.LagDays);
    }

    [Fact]
    public void ComponentVersionOffAnyKnownLineIsNotComparable()
    {
        var swc = Fixtures.Device("Realtek Hardware Support Application", "11.0.6000.398", "Realtek Semiconductor Corp.", "oem120.inf",
            [@"SWC\VEN_10EC&HID_0001"]);

        var r = _matcher.Match(swc);

        Assert.Equal(DriverStatus.NotComparable, r.Status);
        Assert.Equal("Realtek Audio", r.Family?.Name);
    }

    [Fact]
    public void WaterOnTheDirectlyFollowingLineIsTheUpdate()
    {
        // AMD INF 31.x -> 32.x is the next driver generation, not a parallel line.
        var igpu = Fixtures.Device("AMD Radeon(TM) Graphics", "31.0.24002.92", "Advanced Micro Devices, Inc.", "oem41.inf",
            [@"PCI\VEN_1002&DEV_164E&SUBSYS_7D731462&REV_CB", @"PCI\VEN_1002&DEV_164E&SUBSYS_7D731462"],
            [@"PCI\VEN_1002&DEV_164E&REV_CB", @"PCI\VEN_1002&DEV_164E"]);

        var r = _matcher.Match(igpu);

        Assert.Equal(DriverStatus.Behind, r.Status);
        Assert.Equal("31.0.24028.1001", r.LineNewest?.Version.Raw);
        Assert.Equal("32.0.31041.1004", r.Update?.Version.Raw);
        Assert.Equal(new DateOnly(2026, 9, 3).DayNumber - new DateOnly(2025, 5, 10).DayNumber, r.LagDays);
        Assert.Null(r.ParallelLine);
    }

    [Fact]
    public void UnrelatedNewerLineIsOnlyANote()
    {
        // NVIDIA HD Audio 1.4.x shares the NVIDIA Graphics family with the 616.92 driver; that is a note, not an update.
        var audio = Fixtures.Device("NVIDIA High Definition Audio", "1.4.6.3", "NVIDIA Corporation", "oem73.inf",
            [@"PCI\VEN_10DE&DEV_2782"], driverDate: "2026-07-23");

        var r = _matcher.Match(audio);

        Assert.Equal(DriverStatus.Current, r.Status);
        Assert.True(r.IsNewerThanIndex);
        Assert.Equal("616.92", r.ParallelLine?.Version);
    }

    [Fact]
    public void OlderNvidiaBranchIsBehind()
    {
        // Marketing versions are one line: 591.86 -> 616.92 is an update, not a different line.
        var gpu = Fixtures.Device("NVIDIA GeForce RTX 4070 Ti", "32.0.15.9186", "NVIDIA", "oem20.inf",
            [@"PCI\VEN_10DE&DEV_2782&SUBSYS_51301462&REV_A1"], [@"PCI\VEN_10DE&DEV_2782"]);

        var r = _matcher.Match(gpu);

        Assert.Equal(DriverStatus.Behind, r.Status);
        Assert.Equal("591.86", r.Installed?.Raw);
        Assert.Equal("616.92", r.Update?.Version.Raw);
    }

    [Theory]
    [InlineData(31, 32, true)]
    [InlineData(10, 11, true)]
    [InlineData(10, 1125, false)]
    [InlineData(5, 7, false)]
    [InlineData(2, 7, false)]
    [InlineData(32, 31, false)]
    public void SuccessorLines(long from, long to, bool expected) =>
        Assert.Equal(expected, DeviceMatcher.IsSuccessorLine(new(from, 4), new(to, 4)));

    [Fact]
    public void NvidiaInfVersionIsComparedInMarketingScheme()
    {
        var gpu = Fixtures.Device("NVIDIA GeForce RTX 4070 Ti", "32.0.16.1692", "NVIDIA", "oem20.inf",
            [@"PCI\VEN_10DE&DEV_2782&SUBSYS_51301462&REV_A1"], [@"PCI\VEN_10DE&DEV_2782"]);

        var r = _matcher.Match(gpu);

        Assert.Equal(DriverStatus.Current, r.Status);
        Assert.Equal("616.92", r.Installed?.Raw);
        Assert.Equal(MatchConfidence.ExactVersion, r.Confidence);
    }

    [Fact]
    public void LaterBuildOfTheSameReleaseIsAnUpdateWhateverTheListingDates()
    {
        // Legion Slim 5: Bluetooth 26.40.2.228 vs 26.40.3.65 — Lenovo listed the older build after WU listed the newer one.
        var family = new Api.Family { FamilyId = 56, Name = "MediaTek Bluetooth (Wi-Fi 6E)", Component = "bluetooth", Hwids = [@"USB\VID_0489&PID_E0D8&MI_00"] };
        Api.Artefact A(int id, string vendor, string v, string d) =>
            new() { ArtefactId = id, Vendor = vendor, Kind = "driver", FamilyId = 56, VersionRaw = v, ReleaseDate = d, Url = $"https://example.invalid/{v}" };
        var index = DriverIndex.Build([family], [], [A(1, "wucatalog", "26.40.3.65", "2026-06-09"), A(2, "lenovo", "26.40.2.228", "2026-07-01")]);
        var bt = Fixtures.Device("RZ616 Bluetooth(R) Adapter", "26.40.2.228", "Mediatek Inc.", "oem184.inf", [@"USB\VID_0489&PID_E0D8&MI_00"]);

        var r = new DeviceMatcher(index).Match(bt);

        Assert.Equal(DriverStatus.Behind, r.Status);
        Assert.Equal("26.40.3.65", r.Update?.Raw);
        Assert.Null(r.LagDays);
        Assert.True(r.UpdateOnlyFromCatalog);   // only the Catalog lists it: some OEM's machine is offered it
    }

    [Fact]
    public void HigherVersionReleasedEarlierIsTreatedAsSiblingNotUpdate()
    {
        // AMD GPIO 2.2.0.136 (2025) vs 2.9.1.1935 (2023): a different component on the same major line.
        var gpio = Fixtures.Device("AMD GPIO Controller", "2.2.0.136", "Advanced Micro Devices, Inc", "oem99.inf",
            [@"ACPI\VEN_AMDI&DEV_0030", @"ACPI\AMDI0030"], driverDate: "2025-10-11");

        var r = _matcher.Match(gpio);

        Assert.Equal(DriverStatus.Current, r.Status);
        Assert.Equal("2.9.1.1935", r.LineNewest?.Version.Raw);
    }

    [Fact]
    public void BetaListingsAreNeverTheLineNewest()
    {
        var index = Fixtures.Index();
        var chipset = index.ByName("AMD Chipset")!;
        var line8 = Versions.DriverVersion.Parse("8.03.25.247").Line!.Value;

        Assert.Null(chipset.NewestOnLine(line8));
    }

    [Fact]
    public void MalformedIndexIdsAreIgnored() =>
        Assert.Empty(Fixtures.Index().Lookup(@"PCI\AMDPCIE.DEVICEDESC"));

    [Fact]
    public void UnknownDeviceIsNotIndexed()
    {
        var r = _matcher.Match(Fixtures.Device("G815", "1.1.91.2790", "Logitech", "oem113.inf", [@"USB\VID_046D&PID_C33F&REV_3102"]));
        Assert.Equal(DriverStatus.NotIndexed, r.Status);
    }
}
