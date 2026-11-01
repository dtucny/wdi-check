using WdiCheck.Core.Api;
using WdiCheck.Core.Index;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Matching;
using WdiCheck.Core.Reporting;

namespace WdiCheck.Core.Tests;

public class MachineHardwareTests
{
    // This PC's two GPUs: the Ryzen iGPU carries MSI's board subsystem ID (7D73 1462), the RTX card MSI's card ID (5130 1462).
    private static LocalDevice Igpu() => Fixtures.Device("AMD Radeon(TM) Graphics", "31.0.24002.92", "Advanced Micro Devices, Inc.", "oem20.inf",
        [@"PCI\VEN_1002&DEV_164E&SUBSYS_7D731462&REV_CB", @"PCI\VEN_1002&DEV_164E&SUBSYS_7D731462"],
        [@"PCI\VEN_1002&DEV_164E&REV_CB", @"PCI\VEN_1002&DEV_164E"]) with { Class = "Display" };

    private static LocalDevice Rtx4070Ti() => Fixtures.Device("NVIDIA GeForce RTX 4070 Ti", "32.0.15.9186", "NVIDIA", "oem40.inf",
        [@"PCI\VEN_10DE&DEV_2782&SUBSYS_51301462&REV_A1", @"PCI\VEN_10DE&DEV_2782&SUBSYS_51301462"],
        [@"PCI\VEN_10DE&DEV_2782&REV_A1", @"PCI\VEN_10DE&DEV_2782"]) with { Class = "Display" };

    private static LocalDevice AmdSoftware() => Fixtures.Device("AMD-OpenCL User Mode Driver", "31.0.24002.92", "Advanced Micro Devices, Inc.", "oem21.inf",
        [@"PCI\VEN_1002&DEV_164E"]) with { Class = "SoftwareComponent" };

    private static LocalDevice Gpio() => Fixtures.Device("AMD GPIO Controller", "2.2.0.134", "Advanced Micro Devices, Inc", "oem7.inf",
        [@"ACPI\AMDI0030"]) with { Class = "System" };

    private static readonly SystemIdentity Identity = new() { ProcessorName = "AMD Ryzen 7 7800X3D 8-Core Processor" };

    private static IReadOnlyList<HardwareItem> Build(DriverIndex index, params LocalDevice[] devices) =>
        MachineHardware.Build(Identity, new DeviceMatcher(index).MatchAll(devices));

    [Fact]
    public void ProcessorThenEachGpuThenTheRest()
    {
        var items = Build(Fixtures.Index(), Igpu(), Rtx4070Ti(), Fixtures.Rtl8125(), Gpio());

        Assert.Equal(["processor", "graphics", "graphics", "lan"], items.Select(i => i.Component));
        Assert.Equal("AMD Ryzen 7 7800X3D 8-Core Processor", items[0].Name);
        Assert.Equal("AMD GPIO Controller", items[0].Driver?.Device.Name);
        Assert.True(items[0].IsCore && items[1].IsCore && items[2].IsCore);
        Assert.False(items[3].IsCore);
        Assert.Equal("Realtek Gaming 2.5GbE Family Controller", items[3].Name);
    }

    [Fact]
    public void GpusAreBuiltInOrNamedByCardMaker()
    {
        var items = Build(Fixtures.Index(), Igpu(), Rtx4070Ti(), Fixtures.Rtl8125());

        Assert.Equal("built-in", items.Single(i => i.Component == "graphics" && i.Name.StartsWith("AMD")).Detail);
        Assert.Equal("MSI card", items.Single(i => i.Component == "graphics" && i.Name.StartsWith("NVIDIA")).Detail);
    }

    [Fact]
    public void WithoutAKnownBoardSubsystemGpusAreNotLabelled()
    {
        // Only the iGPU carries 7D731462, so nothing says it's the board's.
        var items = Build(Fixtures.Index(), Igpu(), Rtx4070Ti());

        Assert.All(items.Where(i => i.Component == "graphics"), i => Assert.Null(i.Detail));
    }

    [Fact]
    public void GpuVendorSoftwareJoinsTheGpuRow()
    {
        var items = Build(Fixtures.Index(), Igpu(), AmdSoftware());

        var gpu = Assert.Single(items, i => i.Component == "graphics");
        Assert.Equal(2, gpu.Devices.Count);
        Assert.DoesNotContain(items, i => !i.IsCore);
    }

    [Fact]
    public void ChipVendorsOwnNewestReleaseAndDownload()
    {
        var index = DriverIndex.Build(Fixtures.Families, Fixtures.Water, Fixtures.Artefacts.Concat(
        [
            new Artefact { ArtefactId = 9001, Vendor = "silicon", Kind = "driver", FamilyId = 71, VersionRaw = "621.01", ReleaseDate = "2026-10-21",
                Url = "https://www.nvidia.com/download/driverResults.aspx/621.01" },
            new Artefact { ArtefactId = 9002, Vendor = "silicon", Kind = "driver", FamilyId = 71, VersionRaw = "625.10", ReleaseDate = "2026-10-28",
                Url = "https://www.nvidia.com/beta", IsBeta = 1 },
        ]));

        var gpu = Build(index, Rtx4070Ti()).Single(i => i.Component == "graphics");

        Assert.Equal("621.01", gpu.Silicon?.Version);
        Assert.Equal(new DateOnly(2026, 10, 21), gpu.Silicon?.Date);
        Assert.Equal("https://www.nvidia.com/download/driverResults.aspx/621.01", gpu.Silicon?.Url?.AbsoluteUri);
    }

    // AMD 26.10.2 (2026-11-01 publish): two installers, two branches. "-a" (u0205498.inf) binds Raphael's DEV_164E,
    // "-b" (u0205499.inf, the family's newest) only RDNA3/4 such as the 780M's DEV_15BF.
    private static LocalDevice Raphael() => Fixtures.Device("AMD Radeon(TM) Graphics", "32.0.21045.5002", "Advanced Micro Devices, Inc.", "oem132.inf",
        [@"PCI\VEN_1002&DEV_164E&SUBSYS_7D731462&REV_CB"], [@"PCI\VEN_1002&DEV_164E"], original: "u0203303.inf") with { Class = "Display" };

    private static readonly Artefact AmdPage = new()
    {
        ArtefactId = 9101, Vendor = "silicon", Kind = "driver", FamilyId = 3, VersionRaw = "32.0.32015.12012", ReleaseDate = "2026-10-30",
        Url = "https://www.amd.com/en/support/download/drivers.html",
    };

    private static InfRow AmdInf(string name, string ver, long[] norm, string hwid, bool silicon, string cls = "Display") => new()
    {
        InfName = name, DriverVer = ver, DriverVerNormalised = [.. norm], DriverDate = "2026-10-28", Class = cls,
        FamilyIds = [3], Hwids = [hwid], FirstPublished = silicon ? "2026-10-30" : "2026-06-01",
        Sources = silicon ? ["silicon"] : null,
    };

    private static HardwareItem RaphaelRow(params InfRow[] rows)
    {
        var index = DriverIndex.Build(Fixtures.Families, Fixtures.Water, Fixtures.Artefacts.Append(AmdPage));
        var infs = InfIndex.Build(rows, index.Families.Values.SelectMany(f => f.Versions).SelectMany(v => v.Artefacts));
        var results = new DeviceMatcher(index, infs).MatchAll([Raphael()]);
        return MachineHardware.Build(Identity, results, infs).Single(i => i.Component == "graphics");
    }

    [Fact]
    public void ChipVendorsReleaseIsTheInstallerThatBindsTheDevice()
    {
        var row = RaphaelRow(
            AmdInf("u0203303.inf", "32.0.21045.5002", [32, 0, 21045, 5002], @"PCI\VEN_1002&DEV_164E", silicon: false),
            AmdInf("u0205498.inf", "32.0.21045.20006", [32, 0, 21045, 20006], @"PCI\VEN_1002&DEV_164E", silicon: true),
            AmdInf("u0205499.inf", "32.0.32015.12012", [32, 0, 32015, 12012], @"PCI\VEN_1002&DEV_15BF", silicon: true));

        Assert.Equal(DriverStatus.Behind, row.Driver?.Status);
        Assert.Equal("32.0.21045.20006", row.Driver?.Newest?.Raw);
        Assert.Equal("32.0.21045.20006", row.Silicon?.Version);
        Assert.Equal(new DateOnly(2026, 10, 30), row.Silicon?.Date);
        Assert.True(row.Silicon?.SameNumbering);
        Assert.Equal(AmdPage.Url, row.Silicon?.Url?.AbsoluteUri);
        // The INF row has no artefacts of its own; the update links AMD's listing from the same day.
        var link = Assert.Single(DownloadLinks.For(row.Driver?.Update, "msi"));
        Assert.Equal("u0205498.inf 32.0.21045.20006", link.Contains);
    }

    [Fact]
    public void FamilysNewestIsNotOfferedWhenItsInstallerDoesNotBindTheDevice()
    {
        var row = RaphaelRow(
            AmdInf("u0203303.inf", "32.0.21045.5002", [32, 0, 21045, 5002], @"PCI\VEN_1002&DEV_164E", silicon: false),
            AmdInf("u0205499.inf", "32.0.32015.12012", [32, 0, 32015, 12012], @"PCI\VEN_1002&DEV_15BF", silicon: true));

        Assert.Null(row.Silicon);
    }

    [Fact]
    public void GpuRowDescribesTheGpuNotAComponentFurtherBehind()
    {
        // This PC: AMD's noise-suppression component (1.4.40.0, 106 d behind) lags more than the iGPU (72 d).
        var noise = Fixtures.Device("AMD-Dynamic Audio Noise Supression", "1.4.40.0", "Advanced Micro Devices, Inc.", "oem133.inf",
            [@"PCI\VEN_1002&DEV_164E"], original: "amdnoise.inf", driverDate: "2026-03-01") with { Class = "SoftwareComponent" };
        var index = DriverIndex.Build(Fixtures.Families, Fixtures.Water, Fixtures.Artefacts.Append(AmdPage));
        var infs = InfIndex.Build(
            [
                AmdInf("u0205498.inf", "32.0.21045.20006", [32, 0, 21045, 20006], @"PCI\VEN_1002&DEV_164E", silicon: true),
                AmdInf("amdnoise.inf", "1.4.41.0", [1, 4, 41, 0], @"PCI\VEN_1002&DEV_164E", silicon: true, "SoftwareComponent"),
            ],
            index.Families.Values.SelectMany(f => f.Versions).SelectMany(v => v.Artefacts));
        var gpu = Raphael() with { DriverDate = DateTimeOffset.Parse("2026-08-17T00:00:00Z") };

        var row = MachineHardware.Build(Identity, new DeviceMatcher(index, infs).MatchAll([noise, gpu]), infs)
            .Single(i => i.Component == "graphics");

        Assert.Equal(2, row.Devices.Count);
        Assert.Equal("AMD Radeon(TM) Graphics", row.Driver?.Device.Name);
        Assert.Equal("32.0.21045.20006", row.Silicon?.Version);
    }

    // Realtek (2026-11-01 publish, WinDriverIndex#15): its page lists NDIS at the INF's 10.080.50.0407 (the build ASRock
    // and Dell ship) beside NetAdapterCx 1125.031.50.0603, and the rt640x64.inf row is now marked silicon too.
    [Fact]
    public void ChipVendorsReleaseOnTheInstalledLineWithItsOwnDateAndLink()
    {
        var realtekPage = "https://www.realtek.com/Download/List?cate_id=584";
        var index = DriverIndex.Build(Fixtures.Families, Fixtures.Water, Fixtures.Artefacts.Concat(
        [
            new Artefact { ArtefactId = 9201, Vendor = "silicon", Kind = "driver", FamilyId = 88, VersionRaw = "1125.031.50.0603", ReleaseDate = "2026-10-05", Url = realtekPage },
            new Artefact { ArtefactId = 9202, Vendor = "silicon", Kind = "driver", FamilyId = 88, VersionRaw = "10.080.50.0407", ReleaseDate = "2026-10-05", Url = realtekPage },
            new Artefact { ArtefactId = 9203, Vendor = "dell", Kind = "driver", FamilyId = 88, VersionRaw = "1168.030.0508.2026", ReleaseDate = "2026-09-07",
                Url = "https://dl.dell.com/FOLDER13000000M/1/Realtek-LAN.exe" },
        ]));
        var infs = InfIndex.Build(
            [
                new InfRow
                {
                    InfName = "rt640x64.inf", DriverVer = "10.080.50.0407", DriverVerNormalised = [10, 80, 50, 407], DriverDate = "2026-04-07",
                    Class = "Net", FamilyIds = [88], Hwids = [@"PCI\VEN_10EC&DEV_8125"], ArtefactIds = [9203],
                    FirstPublished = "2026-09-07", Sources = ["silicon", "vendor"],
                },
            ],
            index.Families.Values.SelectMany(f => f.Versions).SelectMany(v => v.Artefacts));
        var nic = Fixtures.Rtl8125() with { OriginalInfName = "rt640x64.inf", Class = "Net" };

        var result = new DeviceMatcher(index, infs).Match(nic);
        var row = MachineHardware.Build(Identity, [result], infs).Single(i => i.Component == "lan");

        Assert.Equal("10.080.50.0407", MachineHardware.NewestListedBySilicon(result.Family!, nic.DriverVersion)?.Version);
        Assert.Equal("10.080.50.0407", row.Silicon?.Version);
        Assert.Equal(new DateOnly(2026, 10, 5), row.Silicon?.Date);
        Assert.True(row.Silicon?.SameNumbering);
        // Realtek's own listing is among the update's links, beside Dell's package.
        Assert.Contains(DownloadLinks.For(result.Update, "msi"), l => l.Vendor == "silicon" && l.Url.AbsoluteUri == realtekPage);
        Assert.Contains(DownloadLinks.For(result.Update, "msi"), l => l.Vendor == "dell");
    }

    [Fact]
    public void NoSiliconListingMeansNoSiliconRelease() =>
        Assert.All(Build(Fixtures.Index(), Igpu(), Rtx4070Ti()), i => Assert.Null(i.Silicon));
}
