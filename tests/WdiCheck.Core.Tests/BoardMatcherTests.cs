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
    public void BiosBehindWhenVendorListsNewerDate()
    {
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

        Assert.Equal(BiosStatus.Behind, a.BiosStatus);
        Assert.Equal(73, a.BiosLagDays);
        Assert.Equal(233, a.WorstListingLag);
        Assert.Equal("10.79.50.1003", a.Families.Single(f => f.Row.Family == "Realtek 8125 LAN").InstalledSummary);
        Assert.Equal("—", a.Families.Single(f => f.Row.Family == "AMD RAID").InstalledSummary);
    }

    [Fact]
    public void BiosCurrentWhenInstalledIsNewest()
    {
        var report = new BoardReport { Board = new Board(), Bios = new BoardBios { LastBios = "2026-06-28" } };
        var identity = new SystemIdentity { BiosReleaseDate = new DateOnly(2026, 6, 28) };

        Assert.Equal(BiosStatus.Current, BoardAssessment.Create(report, identity, []).BiosStatus);
    }
}
