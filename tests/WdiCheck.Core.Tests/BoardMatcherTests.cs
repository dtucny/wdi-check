using WdiCheck.Core.Api;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Matching;

namespace WdiCheck.Core.Tests;

public class BoardMatcherTests
{
    // Real boards.json rows (2026-09-22).
    private static readonly Board[] Boards =
    [
        B(1682, "msi", "290297", "MPG B650I EDGE WIFI", "motherboard"),
        B(418, "msi", "290100", "MPG B650 EDGE WIFI", "motherboard"),
        B(1684, "msi", "290400", "MPG B760I EDGE WIFI", "motherboard"),
        B(1, "gigabyte", "10917", "B550 X GAMING (rev. 1.x)", "motherboard"),
        B(2, "gigabyte", "10918", "B550 X GAMING (rev. 2.x)", "motherboard"),
        B(3000, "lenovo", "10sv", "ThinkCentre M720S", "desktop"),
        B(3001, "lenovo", "21hm", "ThinkPad X1 Carbon Gen 11", "laptop"),
        B(4000, "hp", "81c5/hp-z4-g4-workstation", "HP Z4 G4 Workstation", "desktop"),
        B(5000, "dell", "precision-precision-5470", "Precision Precision 5470", "laptop"),
        B(6000, "msi", "10000", "GeForce RTX 4070 Ti GAMING X TRIO 12G", "graphics-card"),
    ];

    [Fact]
    public void MsiBaseboardWithProductCodeMatchesExactly()
    {
        var id = new SystemIdentity
        {
            BaseBoardManufacturer = "Micro-Star International Co., Ltd.",
            BaseBoardProduct = "MPG B650I EDGE WIFI (MS-7D73)",
            SystemManufacturer = "Micro-Star International Co., Ltd.",
            SystemProductName = "MS-7D73",
        };

        var ranked = BoardMatcher.Rank(id, Boards);

        Assert.Equal(1682, ranked[0].Board.BoardId);
        Assert.Equal(1682, BoardMatcher.AutoSelect(ranked)?.Board.BoardId);
        Assert.DoesNotContain(ranked, c => c.Board.ProductType == "graphics-card");
    }

    [Fact]
    public void GigabyteRevisionFollowsBaseboardVersion()
    {
        var id = new SystemIdentity
        {
            BaseBoardManufacturer = "Gigabyte Technology Co., Ltd.",
            BaseBoardProduct = "B550 X GAMING",
            BaseBoardVersion = "2.0",
            SystemManufacturer = "Gigabyte Technology Co., Ltd.",
            SystemProductName = "B550 X GAMING",
        };

        var auto = BoardMatcher.AutoSelect(BoardMatcher.Rank(id, Boards));

        Assert.Equal(2, auto?.Board.BoardId);
    }

    [Fact]
    public void LenovoMatchesByMachineType()
    {
        var id = new SystemIdentity { SystemManufacturer = "LENOVO", SystemProductName = "10SV0012US", BaseBoardProduct = "3136" };

        var auto = BoardMatcher.AutoSelect(BoardMatcher.Rank(id, Boards));

        Assert.Equal(3000, auto?.Board.BoardId);
    }

    [Fact]
    public void LenovoFallsBackToFriendlyName()
    {
        var id = new SystemIdentity { SystemManufacturer = "LENOVO", SystemProductName = "99ZZ0000XX", SystemVersion = "ThinkPad X1 Carbon Gen 11" };

        Assert.Equal(3001, BoardMatcher.Rank(id, Boards)[0].Board.BoardId);
    }

    [Fact]
    public void HpMatchesByPlatformId()
    {
        var id = new SystemIdentity { SystemManufacturer = "HP", SystemProductName = "HP Z4 G4 Workstation", BaseBoardProduct = "81C5" };

        Assert.Equal(4000, BoardMatcher.AutoSelect(BoardMatcher.Rank(id, Boards))?.Board.BoardId);
    }

    [Fact]
    public void DellMatchesWhenListingRepeatsTheSeries()
    {
        var id = new SystemIdentity { SystemManufacturer = "Dell Inc.", SystemProductName = "Precision 5470", BaseBoardProduct = "0XXYYZ" };

        Assert.Equal(5000, BoardMatcher.AutoSelect(BoardMatcher.Rank(id, Boards))?.Board.BoardId);
    }

    [Fact]
    public void UnknownVendorHasNoCandidates()
    {
        var id = new SystemIdentity { SystemManufacturer = "Framework", SystemProductName = "Laptop 13", BaseBoardManufacturer = "Framework", BaseBoardProduct = "FRANMECP05" };

        Assert.Empty(BoardMatcher.Rank(id, Boards));
    }

    [Fact]
    public void SimilarNamesAreCandidatesButNotAutoSelected()
    {
        var id = new SystemIdentity { BaseBoardManufacturer = "Micro-Star International Co., Ltd.", BaseBoardProduct = "MPG B650M EDGE WIFI (MS-7D99)" };

        var ranked = BoardMatcher.Rank(id, Boards);

        Assert.NotEmpty(ranked);
        Assert.Null(BoardMatcher.AutoSelect(ranked));
    }

    [Theory]
    [InlineData("MPG B650I EDGE WIFI (MS-7D73)", "MPG B650I EDGE WIFI")]
    [InlineData("B650M-HDV/M.2", "B650M HDV M 2")]
    [InlineData("GeForce RTX™ 3060", "GEFORCE RTX 3060")]
    public void Normalise(string input, string expected) => Assert.Equal(expected, BoardMatcher.Normalise(input));

    [Fact]
    public void DisplayNameSkipsRedundantBoardCode()
    {
        var id = new SystemIdentity
        {
            BaseBoardManufacturer = "Micro-Star International Co., Ltd.",
            BaseBoardProduct = "MPG B650I EDGE WIFI (MS-7D73)",
            SystemManufacturer = "Micro-Star International Co., Ltd.",
            SystemProductName = "MS-7D73",
        };

        Assert.Equal("Micro-Star International Co., Ltd. MPG B650I EDGE WIFI (MS-7D73)", id.DisplayName);
    }

    private static Board B(int id, string vendor, string pid, string name, string type) =>
        new() { BoardId = id, Vendor = vendor, VendorProductId = pid, Name = name, ProductType = type };
}

public class BoardAssessmentTests
{
    [Fact]
    public void BiosPublishedSoonAfterBuildIsUnconfirmed()
    {
        // Dates only: MSI's newest is dated 73 days after this BIOS's build date — possibly the same release.
        var report = new BoardReport
        {
            Board = new Board { BoardId = 1682, Vendor = "msi", Name = "MPG B650I EDGE WIFI" },
            Bios = new BoardBios { LastBios = "2026-09-09", Agesa = "1.3.0.1d", AgesaLine = "AM5", AgesaAtWater = true },
            Families = [new BoardFamilyRow { Family = "Realtek 8125 LAN", LagDays = 233 }, new BoardFamilyRow { Family = "AMD RAID", LagDays = 191 }],
        };
        var identity = new SystemIdentity { BiosVersion = "1.L2", BiosReleaseDate = new DateOnly(2026, 6, 28) };
        var matcher = new DeviceMatcher(Fixtures.Index());
        var results = new[] { matcher.Match(Fixtures.Rtl8125()) };

        var a = BoardAssessment.Create(report, identity, results);

        Assert.Equal(BiosStatus.Unconfirmed, a.BiosStatus);
        Assert.Equal(73, a.BiosLagDays);
        Assert.Equal(233, a.WorstListingLag);
        Assert.Equal("10.79.50.1003", a.Families.Single(f => f.Row.Family == "Realtek 8125 LAN").InstalledSummary);
        Assert.Equal("—", a.Families.Single(f => f.Row.Family == "AMD RAID").InstalledSummary);
    }

    [Theory]
    // Legion Slim 5: M3CN50WW built 2026-04-23, published by Lenovo 2026-08-07 — Vantage confirms it is the newest.
    [InlineData("2026-08-07", null, "M3CN50WW", "2026-04-23", BiosStatus.Unconfirmed)]
    [InlineData("2026-08-07", "M3CN50WW", "M3CN50WW", "2026-04-23", BiosStatus.Current)]
    [InlineData("2026-08-07", "M3CN50WW", "M3CN49WW", "2026-02-10", BiosStatus.Behind)]
    [InlineData("2026-08-07", null, "M3CN41WW", "2023-12-01", BiosStatus.Behind)]
    [InlineData("2026-08-07", null, "M3CN51WW", "2026-09-01", BiosStatus.Current)]
    [InlineData(null, null, "M3CN50WW", "2026-04-23", BiosStatus.Unknown)]
    // MSI lists "7D73v1L2" for what SMBIOS reports as "1.L2".
    [InlineData("2026-09-09", "7D73v1L2", "1.L2", "2026-06-28", BiosStatus.Current)]
    // This PC on 2026-09-29: MSI's newest is 7D73v1L3, installed 1.L2 — genuinely one BIOS behind.
    [InlineData("2026-09-09", "7D73v1L3", "1.L2", "2026-06-28", BiosStatus.Behind)]
    // Formats that can't be compared (different shapes): the date heuristic decides.
    [InlineData("2026-09-09", "1.24.0", "A24", "2026-06-28", BiosStatus.Unconfirmed)]
    // HP SMBIOS "S70 Ver. 01.29.00" vs the site's "01.29.00".
    [InlineData("2026-05-01", "01.29.00", "S70 Ver. 01.29.00", "2026-03-15", BiosStatus.Current)]
    // Case and punctuation don't matter.
    [InlineData("2026-08-07", "m3cn50ww", " M3CN50WW ", "2026-04-23", BiosStatus.Current)]
    public void BiosVersionIsAuthoritativeAndDatesAreHedged(string? vendorDate, string? vendorVersion, string installed, string built, BiosStatus expected)
    {
        var report = new BoardReport { Board = new Board(), Bios = new BoardBios { LastBios = vendorDate, LastBiosVersion = vendorVersion } };
        var identity = new SystemIdentity { BiosVersion = installed, BiosReleaseDate = DateOnly.Parse(built) };

        Assert.Equal(expected, BoardAssessment.Create(report, identity, []).BiosStatus);
    }

    [Theory]
    [InlineData("M3CN50WW", "M3CN50WW", "Same")]
    [InlineData("7D73v1L2", "1.L2", "Same")]
    [InlineData("01.29.00", "S70 Ver. 01.29.00", "Same")]
    [InlineData("M3CN50WW", "M3CN49WW", "Different")]
    [InlineData("F12", "F11", "Different")]
    [InlineData("7D73v1L3", "1.L2", "Different")]
    [InlineData("S70 Ver. 01.29.00", "01.28.00", "Different")]
    [InlineData("1.24.0", "A24", "Incomparable")]
    [InlineData("F12", "F2", "Incomparable")] // a 2-character tail is too short to trust
    [InlineData("", "1.L2", "Incomparable")]
    public void BiosVersionFormats(string vendor, string installed, string expected) =>
        Assert.Equal(expected, BoardAssessment.CompareBiosVersions(vendor, installed).ToString());

    [Fact]
    public void BiosCurrentWhenInstalledIsNewest()
    {
        var report = new BoardReport { Board = new Board(), Bios = new BoardBios { LastBios = "2026-06-28" } };
        var identity = new SystemIdentity { BiosReleaseDate = new DateOnly(2026, 6, 28) };

        Assert.Equal(BiosStatus.Current, BoardAssessment.Create(report, identity, []).BiosStatus);
    }
}
