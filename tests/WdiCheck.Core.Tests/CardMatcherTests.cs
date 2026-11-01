using WdiCheck.Core.Api;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Matching;

namespace WdiCheck.Core.Tests;

public class CardMatcherTests
{
    // This PC: the iGPU and NIC carry MSI's board subsystem ID (7D73 1462); the RTX card MSI's card ID (5130 1462).
    private static LocalDevice Igpu() => Fixtures.Device("AMD Radeon(TM) Graphics", "31.0.24002.92", "Advanced Micro Devices, Inc.", "oem20.inf",
        [@"PCI\VEN_1002&DEV_164E&SUBSYS_7D731462&REV_CB"]) with { Class = "Display" };

    private static LocalDevice Rtx4070Ti() => Fixtures.Device("NVIDIA GeForce RTX 4070 Ti", "32.0.15.9186", "NVIDIA", "oem40.inf",
        [@"PCI\VEN_10DE&DEV_2782&SUBSYS_51301462&REV_A1", @"PCI\VEN_10DE&DEV_2782&SUBSYS_51301462"]) with { Class = "Display" };

    private static Board Card(int id, string vendor, string name, string subsysVendor, string[] devices, string[]? subsys = null) => new()
    {
        BoardId = id, Vendor = vendor, Name = name, ProductType = "graphics-card",
        Pci = new BoardPci { Device = [.. devices], SubsysVendor = subsysVendor, Subsys = subsys is null ? null : [.. subsys] },
    };

    // Real catalogue entries (2026-11-01 publish): MSI's 4070 Ti cards share 10DE:2782 and 1462.
    private static readonly Board[] Catalogue =
    [
        Card(3878, "msi", "GeForce RTX 4070 Ti GAMING TRIO 12G", "1462", ["10DE:2782"]),
        Card(3879, "msi", "GeForce RTX 4070 Ti GAMING X TRIO 12G", "1462", ["10DE:2782"]),
        Card(3914, "msi", "GeForce RTX 4070 Ti SUPER 16G GAMING X TRIO WHITE", "1462", ["10DE:2689", "10DE:2705"]),
        Card(5001, "asus", "TUF Gaming GeForce RTX 4070 Ti 12GB", "1043", ["10DE:2782"]),
        new Board { BoardId = 1682, Vendor = "msi", Name = "MPG B650I EDGE WIFI", ProductType = "motherboard" },
    ];

    private static IReadOnlyList<GraphicsCard> Find(Board[] catalogue, params LocalDevice[] devices) =>
        CardMatcher.Find(new DeviceMatcher(Fixtures.Index()).MatchAll(devices), catalogue);

    [Fact]
    public void AddInCardWithTheMakersModelsOnItsGpu()
    {
        var card = Assert.Single(Find(Catalogue, Igpu(), Rtx4070Ti(), Fixtures.Rtl8125()));

        Assert.Equal(@"PCI\VEN_10DE&DEV_2782&SUBSYS_51301462", card.Key);
        Assert.Equal("MSI", card.Maker);
        Assert.Equal("5130", card.SubsysDevice);
        Assert.Equal([3878, 3879], card.Candidates.Select(b => b.BoardId));
    }

    [Fact]
    public void NoBoardSubsystemNoCards() =>
        // Only the iGPU carries 7D731462, so it can't be told from a card.
        Assert.Empty(Find(Catalogue, Igpu(), Rtx4070Ti()));

    [Fact]
    public void RevisionTellsCardsOnOneDeviceIdApart()
    {
        // Gigabyte: RX 9070 XT GAMING OC ICE is 1002:7550 rev C0/C3, the RX 9070 GRE GAMING OC rev C2.
        var cards = new[]
        {
            Card(3438, "gigabyte", "Radeon RX 9070 GRE GAMING OC 12G", "1458", ["1002:7550:C2"]),
            Card(3439, "gigabyte", "Radeon RX 9070 XT GAMING OC ICE 16G", "1458", ["1002:7550:C0", "1002:7550:C3"]),
        };
        var id = new CardMatcher.PciId("1002", "7550", "2437", "1458", "C0");

        Assert.Equal([3439], CardMatcher.Candidates(id, cards).Select(b => b.BoardId));
    }

    [Fact]
    public void SubsystemIdsPickTheModelOrRuleModelsOut()
    {
        var cards = new[]
        {
            Card(1, "msi", "GAMING TRIO", "1462", ["10DE:2782"], ["5131"]),
            Card(2, "msi", "GAMING X TRIO", "1462", ["10DE:2782"], ["5130"]),
            Card(3, "msi", "VENTUS 3X", "1462", ["10DE:2782"]),
        };
        var mine = new CardMatcher.PciId("10DE", "2782", "5130", "1462", "A1");
        var other = new CardMatcher.PciId("10DE", "2782", "5199", "1462", "A1");

        Assert.Equal([2], CardMatcher.Candidates(mine, cards).Select(b => b.BoardId));
        // 5199 is neither listed ID: it's one of the models without IDs in the index.
        Assert.Equal([3], CardMatcher.Candidates(other, cards).Select(b => b.BoardId));
    }

    private static BoardAssessment Report(int id, BoardBios? bios) => BoardAssessment.Create(
        new BoardReport { Board = new Board { BoardId = id, Vendor = "msi" }, Bios = bios }, new SystemIdentity(), [], assessBios: false);

    [Fact]
    public void ModelIsAskedForOnlyWhenAModelListsAVbios()
    {
        var card = Assert.Single(Find(Catalogue, Igpu(), Rtx4070Ti(), Fixtures.Rtl8125()));

        var sameListing = new CardAssessment { Card = card, Reports = [Report(3878, null), Report(3879, null)] };
        var oneWithVbios = sameListing with { Reports = [Report(3878, null), Report(3879, new BoardBios { LastBiosVersion = "V4" })] };
        var chosen = oneWithVbios with { Selected = Catalogue[1], IsOverride = true };

        Assert.False(sameListing.ModelMatters);
        Assert.True(oneWithVbios.ModelMatters);
        Assert.False(chosen.ModelMatters);
        Assert.Equal(3879, Assert.Single(chosen.Shown).Report.Board.BoardId);
    }
}
