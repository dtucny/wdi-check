using WdiCheck.Core.Api;
using WdiCheck.Core.Index;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Matching;
using WdiCheck.Core.Reporting;
using WdiCheck.Core.Scanning;
using WdiCheck.Core.Versions;

namespace WdiCheck.Core.Tests;

/// <summary>
/// Schema 2.0.0 features: INF-level matching (infs.json), published scheme rules and line data,
/// SMBIOS board keys. Rows are real (2026-09-28 publish); devices are this repo's test PC and
/// a Lenovo Legion Slim 5 16APH8 export.
/// </summary>
public class InfMatchingTests
{
    private static readonly InfRow[] Rows =
    [
        Inf("nvpcf.inf", "32.0.16.1534", [32, 0, 16, 1534], "2026-05-20", "SoftwareDevice", [71], [@"ACPI\NVDA0820"]),
        Inf("nvpcf.inf", "32.0.16.1051", [32, 0, 16, 1051], "2026-06-02", "SoftwareDevice", [71], [@"ACPI\NVDA0820"]),
        Inf("realtekservice.inf", "1.0.1018.0", [1, 0, 1018, 0], "2026-07-14", "SoftwareComponent", [1], [@"SWC\VEN_10EC&SID_0001"]),
        Inf("realtekservice.inf", "1.0.866.0", [1, 0, 866, 0], "2025-03-10", "SoftwareComponent", [1], [@"SWC\VEN_10EC&SID_0001"]),
        Inf("rt640x64.inf", "10.080.50.0407", [10, 80, 50, 407], "2026-04-07", "Net", [87, 88], [@"PCI\VEN_10EC&DEV_8125", @"PCI\VEN_10EC&DEV_8168"]),
        Inf("rt640x64.inf", "10.079.50.1003", [10, 79, 50, 1003], "2025-10-03", "Net", [88], [@"PCI\VEN_10EC&DEV_8125", @"PCI\VEN_10EC&DEV_8168"]),
        Inf("rt25cx21x64.inf", "1125.029.50.0202", [1125, 29, 50, 202], "2026-02-02", "Net", [88], [@"PCI\VEN_10EC&DEV_8125"]),
        Inf("u0399879.inf", "31.0.24002.92", [31, 0, 24002, 92], "2024-01-11", "Display", [3], [@"PCI\VEN_1002&DEV_164E"]),
        Inf("u0393743.inf", "31.0.14070.0", [31, 0, 14070, 0], "2023-11-01", "Display", [3], [@"PCI\VEN_1002&DEV_164E"]),
        Inf("u0403049.inf", "32.0.11002.41", [32, 0, 11002, 41], "2024-05-01", "Display", [3], [@"PCI\VEN_1002&DEV_164E"]),
        Inf("u0199286.inf", "32.0.21043.5001", [32, 0, 21043, 5001], "2026-03-03", "Display", [3], [@"PCI\VEN_1002&DEV_164E"]),
        // Another INF series binding the same ID on the next line: must not become the display driver's target.
        Inf("amdxe.inf", "32.9.0.1", [32, 9, 0, 1], "2026-09-01", "Display", [3], [@"PCI\VEN_1002&DEV_164E"]),
        Inf("realtekapo2.inf", "11.0.6000.833", [11, 0, 6000, 833], "2020-11-16", "SoftwareComponent", [1], [@"SWC\VEN_10EC&AID_0011"]),
        Inf("realtekuapo2.inf", "13.6429.2509.904", [13, 6429, 2509, 904], "2026-08-18", "SoftwareComponent", [1], [@"SWC\VEN_10EC&AID_0011"]),
        // Same file name from an unrelated vendor, binding other hardware: must not match by name alone.
        Inf("netwtw.inf", "99.0.0.1", [99, 0, 0, 1], "2026-01-01", "Net", [], [@"PCI\VEN_8086&DEV_2725"]),
    ];

    private static DeviceMatcher Matcher() =>
        new(Fixtures.Index(), InfIndex.Build(Rows, Fixtures.Artefacts));

    [Fact]
    public void ComponentInfCurrentAtItsOwnNewest()
    {
        // Legion Slim 5: NVIDIA Platform Controllers 32.0.16.1534 was "112 d behind" 616.92 under package matching.
        var pcf = Fixtures.Device("NVIDIA Platform Controllers and Framework", "32.0.16.1534", "NVIDIA", "oem118.inf",
            [@"ACPI\VEN_NVDA&DEV_0820", @"ACPI\NVDA0820"], original: "nvpcf.inf");

        var r = Matcher().Match(pcf);

        Assert.Equal(DriverStatus.Current, r.Status);
        Assert.Equal(MatchConfidence.InfExact, r.Confidence);
        Assert.Equal("nvpcf.inf", r.Inf?.InfName);
        Assert.True(r.Inf!.ByName);
    }

    [Fact]
    public void ComponentInfBehindItsOwnNewest()
    {
        // Legion Slim 5: Realtek Audio Universal Service was compared with an unrelated 1.4.2.55; its INF's newest is 1.0.1018.0.
        var svc = Fixtures.Device("Realtek Audio Universal Service", "1.0.866.0", "Realtek", "oem153.inf",
            [@"SWC\VEN_10EC&SID_0001"], original: "realtekservice.inf", driverDate: "2025-03-10");

        var r = Matcher().Match(svc);

        Assert.Equal(DriverStatus.Behind, r.Status);
        Assert.Equal("1.0.1018.0", r.Update?.Raw);
        Assert.Equal(new DateOnly(2026, 7, 14).DayNumber - new DateOnly(2025, 3, 10).DayNumber, r.LagDays);
        Assert.Equal("Realtek Audio", r.Family?.Name);
    }

    [Fact]
    public void NicComparedWithinItsInfAndNotesTheNetAdapterLine()
    {
        var r = Matcher().Match(Fixtures.Rtl8125() with { OriginalInfName = "rt640x64.inf" });

        Assert.Equal(DriverStatus.Behind, r.Status);
        Assert.Equal(MatchConfidence.InfExact, r.Confidence);
        Assert.Equal("10.080.50.0407", r.Update?.Raw);
        // rt25cx21x64.inf (1125.x) binds the same chip on another line: not an update target, but worth a note.
        Assert.DoesNotContain(r.Inf!.Releases, x => x.InfName == "rt25cx21x64.inf");
        Assert.Equal("1125.29.50.202", r.ParallelLine?.Version);
    }

    [Fact]
    public void RenamedPerReleaseInfFindsNewestBuildListingTheDevice()
    {
        // AMD names display INFs per release (u0401611.inf is not indexed). Within the u*.inf series, the newest
        // build that lists this device is 32.0.21043.5001, not the family water (AMD's 26.x INFs drop RDNA2).
        var gpu = Fixtures.Device("AMD Radeon(TM) Graphics", "31.0.24002.92", "Advanced Micro Devices, Inc.", "oem41.inf",
            [@"PCI\VEN_1002&DEV_164E&SUBSYS_7D731462&REV_CB"], [@"PCI\VEN_1002&DEV_164E&REV_CB", @"PCI\VEN_1002&DEV_164E"],
            original: "u0401611.inf");
        gpu = gpu with { Class = "Display" };

        var r = Matcher().Match(gpu);

        Assert.False(r.Inf!.ByName);
        Assert.Equal(MatchConfidence.InfExact, r.Confidence);
        Assert.Equal(DriverStatus.Behind, r.Status);
        Assert.Equal("32.0.21043.5001", r.Update?.Raw);
        Assert.DoesNotContain(r.Inf.Releases, x => x.InfName == "amdxe.inf");
    }

    [Fact]
    public void WithoutAnInfNameOnlyTheSameLineIsUsed()
    {
        var gpu = Fixtures.Device("AMD Radeon(TM) Graphics", "31.0.24002.92", "Advanced Micro Devices, Inc.", "oem41.inf",
            [@"PCI\VEN_1002&DEV_164E"]) with { Class = "Display" };

        var r = Matcher().Match(gpu);

        Assert.Equal(DriverStatus.Current, r.Status);
        Assert.All(r.Inf!.Releases, x => Assert.Equal(31, x.Version.Major));
    }

    [Fact]
    public void InstalledAliasSpellingIsTheSameBuild()
    {
        // 2.0.x folds Realtek's two DriverVer stamps into one row; 10.080.0407.2026 is an alias of 10.080.50.0407.
        var rows = Rows.Where(r => r.InfName != "rt640x64.inf").Append(new InfRow
        {
            InfName = "rt640x64.inf", DriverVer = "10.080.50.0407", DriverVerNormalised = [10, 80, 50, 407],
            DriverVerAliases = ["10.080.0407.2026"], DriverDate = "2026-04-07", Class = "Net", FamilyIds = [87, 88],
            Hwids = [@"PCI\VEN_10EC&DEV_8125"],
        });
        var matcher = new DeviceMatcher(Fixtures.Index(), InfIndex.Build(rows, Fixtures.Artefacts));

        var r = matcher.Match(Fixtures.Rtl8125("10.80.407.2026") with { OriginalInfName = "rt640x64.inf" });

        Assert.Equal(DriverStatus.Current, r.Status);
        Assert.False(r.IsNewerThanIndex);
        Assert.Equal(MatchConfidence.InfExact, r.Confidence);
    }

    [Theory]
    [InlineData("u0401611.inf", "u.inf")]
    [InlineData("amdwin-u0202607.inf", "amdwin-u.inf")]
    [InlineData(null, null)]
    public void InfSeries(string? name, string? expected) => Assert.Equal(expected, InfIndex.Series(name));

    [Fact]
    public void AnotherOemsInfForTheSameChipIsNotTheTarget()
    {
        // Legion Slim 5: Lenovo's hdxacplv.inf 6.0.9857.1 was compared with MSI's hdxacpmsi.inf 6.0.10007.1 via the shared chip ID.
        var rows = Rows.Append(Inf("hdxacpmsi.inf", "6.0.10007.1", [6, 0, 10007, 1], "2026-06-23", "MEDIA", [1],
            [@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0287"]));
        var matcher = new DeviceMatcher(Fixtures.Index(), InfIndex.Build(rows, Fixtures.Artefacts));
        var audio = Fixtures.Device("Realtek(R) Audio", "6.0.9857.1", "Realtek Semiconductor Corp.", "oem31.inf",
            [@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0287&SUBSYS_17AA3C92&REV_1000"], original: "hdxacplv.inf") with { Class = "MEDIA" };

        Assert.Null(matcher.Match(audio).Inf);
    }

    [Fact]
    public void SiblingInfsOnOtherLinesAreNotMixed()
    {
        // realtekapo2 (11.x) and realtekuapo2 (13.x) bind the same SWC ID; the installed INF decides.
        var apo = Fixtures.Device("Realtek Audio Effects Component", "12.108.1090.105", "Realtek", "oem25.inf",
            [@"SWC\VEN_10EC&AID_0011"], original: "realtekuapo2.inf");

        var r = Matcher().Match(apo);

        Assert.Equal(DriverStatus.Behind, r.Status);
        Assert.Equal("13.6429.2509.904", r.Update?.Raw);
    }

    [Fact]
    public void SameInfNameInAnotherNumberingSchemeFallsBack()
    {
        // Legion Slim 5: RZ616 Wi-Fi runs Windows Update's mtkwl6ex.inf 25.40.2.579; the index lists the vendor's 3.x builds.
        var rows = Rows.Append(Inf("mtkwl6ex.inf", "3.06.02.1427", [3, 6, 2, 1427], "2026-06-10", "Net", [54], [@"PCI\VEN_14C3&DEV_0616"]));
        var matcher = new DeviceMatcher(Fixtures.Index(), InfIndex.Build(rows, Fixtures.Artefacts));
        var wifi = Fixtures.Device("RZ616 Wi-Fi 6E 160MHz", "25.40.2.579", "MediaTek, Inc.", "oem11.inf",
            [@"PCI\VEN_14C3&DEV_0616&SUBSYS_E0C617AA"], original: "mtkwl6ex.inf");

        var r = matcher.Match(wifi);

        Assert.Null(r.Inf);
        Assert.Equal(DriverStatus.NotIndexed, r.Status);
    }

    [Fact]
    public void YearNumberedBuildComparedWithinItsOwnLine()
    {
        // 2.0.x routes Lenovo's year-numbered MediaTek listings (25.40.2.586) to Wi-Fi 6E as a parallel line.
        var families = new[] { new Family { FamilyId = 54, Name = "MediaTek Wi-Fi 6E", Component = "wlan", Hwids = [@"PCI\VEN_14C3&DEV_0616"] } };
        var water = new[]
        {
            new WaterLevel
            {
                FamilyId = 54, Family = "MediaTek Wi-Fi 6E", Version = "3.6.2.1427", FirstPublished = "2026-06-10",
                Lines = [new VersionLineInfo { Major = 3, WaterLine = true }, new VersionLineInfo { Major = 25, ParallelToWater = true }],
            },
        };
        Artefact A(int id, string v, string d) => new() { ArtefactId = id, Vendor = "lenovo", Kind = "driver", FamilyId = 54, VersionRaw = v, ReleaseDate = d, Url = $"https://example.invalid/{v}.exe" };
        var index = DriverIndex.Build(families, water, [A(1, "3.6.2.1427", "2026-06-10"), A(2, "25.40.2.586", "2026-08-01"), A(3, "25.40.2.579", "2026-05-01")]);
        var wifi = Fixtures.Device("RZ616 Wi-Fi 6E 160MHz", "25.40.2.579", "MediaTek, Inc.", "oem11.inf",
            [@"PCI\VEN_14C3&DEV_0616&SUBSYS_E0C617AA"], [@"PCI\VEN_14C3&DEV_0616"], original: "mtkwl6ex.inf");

        var r = new DeviceMatcher(index, InfIndex.Build(Rows, [])).Match(wifi);

        Assert.Equal(DriverStatus.Behind, r.Status);
        Assert.Equal("25.40.2.586", r.Update?.Raw);
        Assert.Equal("3.6.2.1427", r.ParallelLine?.Version);
    }

    [Theory]
    [InlineData(5, 6, true)]
    [InlineData(3, 25, false)]
    [InlineData(10, 1125, false)]
    public void SameSchemeLines(long a, long b, bool expected) =>
        Assert.Equal(expected, new VersionLine(a, 4).IsSameScheme(new VersionLine(b, 4)));

    [Fact]
    public void SameInfNameForOtherHardwareDoesNotMatch()
    {
        var d = Fixtures.Device("Some NIC", "1.0.0.0", "Vendor", "oem9.inf", [@"PCI\VEN_1234&DEV_0001"], original: "netwtw.inf");

        Assert.Equal(DriverStatus.NotIndexed, Matcher().Match(d).Status);
    }

    [Fact]
    public void WithoutInfDataFallsBackToPackageMatching()
    {
        var r = new DeviceMatcher(Fixtures.Index()).Match(Fixtures.Rtl8125() with { OriginalInfName = "rt640x64.inf" });

        Assert.Null(r.Inf);
        Assert.Equal(MatchConfidence.ExactVersion, r.Confidence);
        Assert.Equal("10.080.0525.2026", r.Update?.Raw);
    }

    [Fact]
    public void StrippedIdsIncludeSubsysAndRevFreeForms()
    {
        var ids = Hwid.DeviceIds(Fixtures.Rtl8125());
        Assert.Contains(@"PCI\VEN_10EC&DEV_8125", ids);
        Assert.Contains(@"PCI\VEN_10EC&DEV_8125&CC_020000", ids);
        Assert.DoesNotContain(@"PCI\CC_020000", ids);
        Assert.Equal(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_00A6", Hwid.Strip(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_00A6&SUBSYS_14625130&REV_1001"));
    }

    [Fact]
    public async Task ExportCarriesInfColumns()
    {
        var r = Matcher().Match(Fixtures.Rtl8125() with { OriginalInfName = "rt640x64.inf" });
        using var ms = new MemoryStream();
        await ReportExporter.WriteCsvAsync(ms, [r], TestContext.Current.CancellationToken);
        var text = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        Assert.Contains("original_inf,matched_inf", text);
        Assert.Contains("rt640x64.inf,rt640x64.inf,10.080.50.0407", text);
    }

    private static InfRow Inf(string? name, string ver, long[] norm, string date, string cls, int[] families, string[] hwids) =>
        new()
        {
            InfName = name,
            DriverVer = ver,
            DriverVerNormalised = [.. norm],
            DriverDate = date,
            Class = cls,
            FamilyIds = [.. families],
            Hwids = [.. hwids],
            FirstPublished = date,
        };
}

public class Schema2DataTests
{
    [Theory]
    [InlineData("32.0.15.9186", "591.86")]
    [InlineData("32.0.16.1692", "616.92")]
    public void PublishedNvidiaRuleMatchesBuiltIn(string inf, string marketing)
    {
        var rule = SchemeEquivalence.FromRule(new VersionEquivRule
        {
            Pattern = @"^3\d\.0\.1(\d)\.(\d{2})(\d{2})$", Replace = "$1$2.$3", Flags = "",
        })!;
        Assert.Equal(marketing, rule(inf));
        Assert.Equal(SchemeEquivalence.NvidiaMarketing(inf), rule(inf));
    }

    [Fact]
    public void PublishedRealtekRuleDropsUnmatchedSuffix()
    {
        var rule = SchemeEquivalence.FromRule(new VersionEquivRule { Pattern = @"^(\d{4,5})\.(\d+)[_ ]?UAD", Replace = "6.0.$1.$2", Flags = "i" })!;
        Assert.Equal("6.0.9360.1", rule("9360.1_uad_WHQL_Nahimic"));
        Assert.Null(rule("6.0.9235.1"));
    }

    [Fact]
    public void InvalidPublishedRuleIsIgnored() =>
        Assert.Null(SchemeEquivalence.FromRule(new VersionEquivRule { Pattern = "(unclosed", Replace = "$1" }));

    [Fact]
    public void PublishedRuleOverridesBuiltIn()
    {
        var family = new Family
        {
            FamilyId = 71, Name = "NVIDIA Graphics",
            VersionEquiv = new VersionEquivRule { Pattern = @"^(\d+)\.(\d+)$", Replace = "9.$1.$2" },
        };
        var index = DriverIndex.Build([family], [], []);
        Assert.Equal("9.616.92", index.Families[71].Effective("616.92").Normalised);
    }

    [Fact]
    public void CombinedListingsAreNotComparisonTargets()
    {
        Assert.True(DriverVersion.IsCombined("6.0.9464.1/12.1.6003.5923"));
        Assert.False(DriverVersion.IsCombined("10.80/beta"));
        var family = new Family { FamilyId = 1, Name = "Realtek Audio" };
        var index = DriverIndex.Build([family], [],
            [new Artefact { ArtefactId = 1, Vendor = "lenovo", FamilyId = 1, VersionRaw = "6.0.9464.1/12.1.6003.5923", IsBeta = 0 }]);
        Assert.Empty(index.Families[1].Versions);
    }

    [Fact]
    public void IndexMarkedParallelLineIsNotTreatedAsSuccessor()
    {
        // The index marks AMD's 31.x line as parallel to 32.x, so 32.x is a note, not the update target.
        var water = Fixtures.Water.Select(w => w.FamilyId != 3 ? w : new WaterLevel
        {
            FamilyId = 3, Family = "AMD Graphics", Version = "32.0.31041.1004", FirstPublished = "2026-09-03",
            Lines = [new VersionLineInfo { Major = 32, WaterLine = true }, new VersionLineInfo { Major = 31, ParallelToWater = true }],
        });
        var matcher = new DeviceMatcher(DriverIndex.Build(Fixtures.Families, water, Fixtures.Artefacts));
        var igpu = Fixtures.Device("AMD Radeon(TM) Graphics", "31.0.24002.92", "Advanced Micro Devices, Inc.", "oem41.inf",
            [@"PCI\VEN_1002&DEV_164E&SUBSYS_7D731462&REV_CB"], [@"PCI\VEN_1002&DEV_164E"]);

        var r = matcher.Match(igpu);

        Assert.Equal("31.0.24028.1001", r.Update?.Raw);
        Assert.Equal("32.0.31041.1004", r.ParallelLine?.Version);
    }

    [Theory]
    [InlineData("2026-09-28T04:59:43+00:00", "2026-09-29T01:28:50+00:00", true)]
    [InlineData("2026-09-29T01:28:50+00:00", "2026-09-28T04:59:43+00:00", false)]
    [InlineData("2026-09-29T01:28:50+00:00", "2026-09-29T01:28:50+00:00", false)]
    [InlineData(null, "2026-09-29T01:28:50+00:00", false)]
    public void CachedBoardReportOlderThanIndexIsRechecked(string? report, string? index, bool expected) =>
        Assert.Equal(expected, ScanService.IsOlder(report, index));

    [Theory]
    [InlineData("2.0.0", false)]
    [InlineData("2.3.1", false)]
    [InlineData("3.0.0", true)]
    [InlineData(null, false)]
    public void NewerSchemaMajorIsFlagged(string? version, bool expected) =>
        Assert.Equal(expected, new DataFreshness(null, DateTimeOffset.Now, false, null, true, version).IsNewerSchema);
}

public class SmbiosBoardTests
{
    private static readonly Board[] Boards =
    [
        new() { BoardId = 1, Vendor = "dell", Name = "Dell Pro Laptops Dell Pro 13 Plus PB13250", ProductType = "laptop",
                Smbios = new BoardSmbios { SystemSku = ["0CDB", "0CF3"] } },
        new() { BoardId = 2, Vendor = "hp", Name = "HP Z4 G4 Workstation", VendorProductId = "81c5/hp-z4-g4-workstation", ProductType = "desktop",
                Smbios = new BoardSmbios { BaseboardProduct = ["81C5"] } },
        new() { BoardId = 3, Vendor = "lenovo", Name = "Legion Slim 5 16aph8 (82Y9)", VendorProductId = "82y9", ProductType = "laptop",
                Smbios = new BoardSmbios { SystemProductPrefix = ["82Y9"] } },
        new() { BoardId = 4, Vendor = "msi", Name = "MEG X570 ACE", ProductType = "motherboard",
                Smbios = new BoardSmbios { BaseboardProductContains = ["MS-7C35"] } },
        new() { BoardId = 5, Vendor = "msi", Name = "MEG X570 UNIFY", ProductType = "motherboard",
                Smbios = new BoardSmbios { BaseboardProductContains = ["MS-7C35"] } },
    ];

    [Fact]
    public void DellBySku()
    {
        var id = new SystemIdentity { SystemManufacturer = "Dell Inc.", SystemProductName = "Dell Pro 13 Plus", SystemSku = "0CF3" };
        Assert.Equal(1, BoardMatcher.AutoSelect(BoardMatcher.Rank(id, Boards))?.Board.BoardId);
    }

    [Fact]
    public void LenovoByMtmPrefix()
    {
        // Legion Slim 5: SystemProductName 82Y9, baseboard LNVNB161216.
        var id = new SystemIdentity { SystemManufacturer = "LENOVO", SystemProductName = "82Y9", BaseBoardProduct = "LNVNB161216" };
        Assert.Equal(3, BoardMatcher.AutoSelect(BoardMatcher.Rank(id, Boards))?.Board.BoardId);
    }

    [Fact]
    public void HpByPlatformId()
    {
        var id = new SystemIdentity { SystemManufacturer = "HP", BaseBoardProduct = "81C5" };
        Assert.Equal(2, BoardMatcher.AutoSelect(BoardMatcher.Rank(id, Boards))?.Board.BoardId);
    }

    [Theory]
    [InlineData("MEG X570 UNIFY (MS-7C35)", 5)]
    [InlineData("MEG X570 ACE (MS-7C35)", 4)]
    public void SharedMsiCodeResolvedByName(string product, int expected)
    {
        var id = new SystemIdentity { BaseBoardManufacturer = "Micro-Star International Co., Ltd.", BaseBoardProduct = product };
        Assert.Equal(expected, BoardMatcher.AutoSelect(BoardMatcher.Rank(id, Boards))?.Board.BoardId);
    }

    [Fact]
    public void SharedMsiCodeWithUnknownNameIsNotAutoSelected()
    {
        var id = new SystemIdentity { BaseBoardManufacturer = "Micro-Star International Co., Ltd.", BaseBoardProduct = "MS-7C35" };
        Assert.Null(BoardMatcher.AutoSelect(BoardMatcher.Rank(id, Boards)));
    }
}
