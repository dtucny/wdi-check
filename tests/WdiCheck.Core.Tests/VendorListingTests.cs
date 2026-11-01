using WdiCheck.Core.Api;
using WdiCheck.Core.Index;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Matching;

namespace WdiCheck.Core.Tests;

/// <summary>An installed driver against the board vendor's listing, as opposed to the newest release anywhere.</summary>
public class VendorListingTests
{
    private static readonly SystemIdentity Identity = new();

    private static int ArtefactId(string vendor, string version) =>
        Fixtures.Artefacts.First(a => a.Vendor == vendor && a.VersionRaw == version).ArtefactId;

    // MSI's Realtek LAN download carries rt640x64.inf 10.079.50.1003 (2026-11-01 publish); ASRock's and MSI's newer one 10.080.0525.2026.
    private static InfIndex RealtekInfs() => InfIndex.Build(
        [
            new InfRow
            {
                InfName = "rt640x64.inf", DriverVer = "10.079.50.1003", DriverVerNormalised = [10, 79, 50, 1003], DriverDate = "2025-10-03",
                Class = "Net", FamilyIds = [88], Hwids = [@"PCI\VEN_10EC&DEV_8125"], ArtefactIds = [ArtefactId("msi", "10.79.50.1003")],
            },
            new InfRow
            {
                InfName = "rt640x64.inf", DriverVer = "10.080.0525.2026", DriverVerNormalised = [10, 80, 525, 2026], DriverDate = "2026-05-25",
                Class = "Net", FamilyIds = [88], Hwids = [@"PCI\VEN_10EC&DEV_8125"],
                ArtefactIds = [ArtefactId("asrock", "10.080.0525.2026"), ArtefactId("msi", "10.080.0525.2026")],
            },
        ],
        Fixtures.Artefacts);

    private static DeviceResult Nic(string version, string date) =>
        new DeviceMatcher(Fixtures.Index(), RealtekInfs()).Match(
            Fixtures.Rtl8125(version) with { OriginalInfName = "rt640x64.inf", Class = "Net", DriverDate = DateTimeOffset.Parse(date + "T00:00:00Z") });

    private static BoardFamilyRow Row(string family, string listed, string? equiv = null) =>
        new() { Family = family, ListedVersion = listed, ListedEquiv = equiv, LagDays = 299 };

    [Fact]
    public void SameBuildAsTheVendorEvenWhenTheVendorIsBehindTheNewest()
    {
        // This PC: MSI lists 10.79.50.1003, which is installed; 10.080.0525.2026 is newer elsewhere.
        var nic = Nic("10.79.50.1003", "2025-10-03");

        var c = VendorComparison.Of(nic, Row("Realtek 8125 LAN", "10.79.50.1003"), "msi");

        Assert.Equal(DriverStatus.Behind, nic.Status);
        Assert.Equal(VendorStanding.Same, c.Standing);
        Assert.Equal("10.079.50.1003", c.VendorVersion);
    }

    [Fact]
    public void BehindTheVendorByInfWithTheDaysBetweenBuilds()
    {
        var c = VendorComparison.Of(Nic("10.79.50.1003", "2025-10-03"), Row("Realtek 8125 LAN", "10.080.0525.2026"), "msi");

        Assert.Equal(VendorStanding.Behind, c.Standing);
        Assert.Equal("10.080.0525.2026", c.VendorVersion);
        Assert.Equal(234, c.LagDays);
    }

    [Fact]
    public void NewerThanTheVendor()
    {
        var c = VendorComparison.Of(Nic("10.080.0525.2026", "2026-05-25"), Row("Realtek 8125 LAN", "10.79.50.1003"), "msi");

        Assert.Equal(VendorStanding.Newer, c.Standing);
    }

    [Fact]
    public void OnlyTheBoardVendorsPackagesCount()
    {
        // ASRock lists 10.080.0525.2026 too; on an ASUS board neither package is the vendor's.
        var c = VendorComparison.Of(Nic("10.79.50.1003", "2025-10-03"), Row("Realtek 8125 LAN", "10.080.0525.2026"), "asus");

        // Falls back to the listed version, which is on the installed line: still behind, without INF dates.
        Assert.Equal(VendorStanding.Behind, c.Standing);
        Assert.Null(c.LagDays);
    }

    [Fact]
    public void APackageVersionOnTheInstalledLineIsNotABuildOfTheInf()
    {
        // MSI's AMD chipset package 8.03.25.247 against a component INF at 8.0.0.65: same line, different things.
        // Here: a listed 10.x that isn't any build of rt640x64.inf the index knows.
        var c = VendorComparison.Of(Nic("10.79.50.1003", "2025-10-03"), Row("Realtek 8125 LAN", "10.81.0.1"), "asus");

        Assert.Equal(VendorStanding.Unknown, c.Standing);
    }

    private static DeviceResult Rtx(string version) => new DeviceMatcher(Fixtures.Index()).Match(
        Fixtures.Device("NVIDIA GeForce RTX 4070 Ti", version, "NVIDIA", "oem40.inf",
            [@"PCI\VEN_10DE&DEV_2782&SUBSYS_51301462&REV_A1"], [@"PCI\VEN_10DE&DEV_2782"]) with { Class = "Display" });

    [Theory]
    [InlineData("616.92", VendorStanding.Behind)]
    [InlineData("591.86", VendorStanding.Same)]
    [InlineData("581.15", VendorStanding.Newer)]
    public void PackageLevelInTheFamilysNumbering(string listed, VendorStanding expected)
    {
        // 32.0.15.9186 is 591.86 in NVIDIA's numbering.
        var c = VendorComparison.Of(Rtx("32.0.15.9186"), Row("NVIDIA Graphics", listed), "asus");

        Assert.Equal(expected, c.Standing);
    }

    [Fact]
    public void DifferentNumberingIsUnknown()
    {
        // MSI lists AMD's package version (25.10.36); the iGPU reports its INF version.
        var igpu = new DeviceMatcher(Fixtures.Index()).Match(Fixtures.Device("AMD Radeon(TM) Graphics", "31.0.24002.92", "Advanced Micro Devices, Inc.",
            "oem20.inf", [@"PCI\VEN_1002&DEV_164E&SUBSYS_7D731462&REV_CB"]) with { Class = "Display" });

        var c = VendorComparison.Of(igpu, Row("AMD Graphics", "25.10.36"), "msi");

        Assert.Equal(VendorStanding.Unknown, c.Standing);
        // The vendor's latest is still shown.
        Assert.Equal("25.10.36", c.Listed);
    }

    [Fact]
    public void SeveralDevicesAreBehindIfAnyIs()
    {
        VendorComparison Of(VendorStanding s) => new(s);

        Assert.Equal(VendorStanding.Behind, VendorComparison.Combine([Of(VendorStanding.Newer), Of(VendorStanding.Behind), Of(VendorStanding.Unknown)]).Standing);
        Assert.Equal(VendorStanding.Newer, VendorComparison.Combine([Of(VendorStanding.Same), Of(VendorStanding.Newer)]).Standing);
        Assert.Equal(VendorStanding.Same, VendorComparison.Combine([Of(VendorStanding.Same), Of(VendorStanding.Unknown)]).Standing);
        Assert.Equal(VendorStanding.Unknown, VendorComparison.Combine([]).Standing);
    }

    [Fact]
    public void BoardAssessmentComparesEachInstalledDevice()
    {
        var nic = Nic("10.79.50.1003", "2025-10-03");
        var report = new BoardReport
        {
            Board = new Board { BoardId = 1682, Vendor = "msi", Name = "MPG B650I EDGE WIFI" },
            Families = [Row("Realtek 8125 LAN", "10.79.50.1003")],
        };

        var assessment = BoardAssessment.Create(report, Identity, [nic]);

        Assert.Equal(VendorStanding.Same, assessment.ForDevice(nic)?.Standing);
        Assert.Equal(VendorStanding.Same, assessment.Families.Single().Vendor.Standing);
    }

    [Fact]
    public void NothingInstalledSaysWhy()
    {
        // This PC: the SATA controller runs Windows' mshdc.inf; the Wi-Fi card is disabled.
        var sata = new DeviceMatcher(Fixtures.Index()).Match(Fixtures.Device("Standard SATA AHCI Controller", "10.0.26100.8972", "Microsoft",
            "mshdc.inf", [@"PCI\VEN_1022&DEV_43BD&CC_0104"]));
        var report = new BoardReport
        {
            Board = new Board { BoardId = 1682, Vendor = "msi" },
            Families = [Row("AMD RAID", "9.3.3.218"), Row("MediaTek Wi-Fi 6E", "3.05.00.1415")],
        };

        var families = BoardAssessment.Create(report, Identity, [sata]).Families;

        Assert.Equal(DriverStatus.Inbox, sata.Status);
        Assert.Equal(["Windows driver", "No device found"], families.Select(f => f.InstalledSummary));
    }

    [Fact]
    public void ACardDoesNotTakeTheBoardsBios()
    {
        var report = new BoardReport
        {
            Board = new Board { BoardId = 3878, Vendor = "msi", Name = "GeForce RTX 4070 Ti GAMING TRIO 12G" },
            Bios = new BoardBios { LastBios = "2023-01-05", LastBiosVersion = "V4" },
        };
        var identity = new SystemIdentity { BiosVersion = "1.L2", BiosReleaseDate = new DateOnly(2022, 6, 28) };

        Assert.Equal(BiosStatus.Unknown, BoardAssessment.Create(report, identity, [], assessBios: false).BiosStatus);
        Assert.Equal(BiosStatus.Behind, BoardAssessment.Create(report, identity, []).BiosStatus);
    }
}
