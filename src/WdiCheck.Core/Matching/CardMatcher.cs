using System.Text.RegularExpressions;
using WdiCheck.Core.Api;

namespace WdiCheck.Core.Matching;

/// <summary>An add-in graphics card in this PC and the catalogue entries it could be.</summary>
public sealed record GraphicsCard
{
    public required DeviceResult Gpu { get; init; }

    /// <summary><c>PCI\VEN_10DE&amp;DEV_2782&amp;SUBSYS_51301462</c>: what the user's choice of model is remembered by.</summary>
    public required string Key { get; init; }

    /// <summary>The card maker's PCI vendor ID ("1462") and its own ID for the card design ("5130").</summary>
    public required string SubsysVendor { get; init; }
    public required string SubsysDevice { get; init; }

    /// <summary>"MSI"; null for a maker WdiCheck doesn't know by ID.</summary>
    public string? Maker { get; init; }

    /// <summary>Cards with this GPU (and revision) from this maker, narrowed by subsystem ID where the index has them.</summary>
    public IReadOnlyList<Board> Candidates { get; init; } = [];
}

/// <summary>
/// Finds add-in graphics cards (a GPU whose subsystem ID isn't the board's) and the catalogue's cards they could be.
/// The index identifies a card by GPU device ID, revision and the maker's subsystem vendor, which a maker's models on
/// one chip share; subsystem device IDs (WinDriverIndex#12) narrow that down when present.
/// </summary>
public static partial class CardMatcher
{
    public const string ProductType = "graphics-card";

    public static IReadOnlyList<GraphicsCard> Find(IReadOnlyList<DeviceResult> results, IReadOnlyList<Board> boards)
    {
        // Without the board's own subsystem ID an iGPU can't be told from a card; claim none rather than guess.
        if (MachineHardware.BoardSubsystem(results) is not { } boardSsid)
            return [];
        var catalogue = boards.Where(b => string.Equals(b.ProductType, ProductType, StringComparison.OrdinalIgnoreCase)).ToList();
        return results
            .Where(r => MachineHardware.IsGpu(r.Device))
            .Select(r => (Gpu: r, Id: Parse(r.Device)))
            .Where(x => x.Id is { } id && id.SubsysDevice + id.SubsysVendor != boardSsid)
            .DistinctBy(x => x.Id!.Value.Key)
            .Select(x => new GraphicsCard
            {
                Gpu = x.Gpu,
                Key = x.Id!.Value.Key,
                SubsysVendor = x.Id.Value.SubsysVendor,
                SubsysDevice = x.Id.Value.SubsysDevice,
                Maker = MachineHardware.PciVendorName(x.Id.Value.SubsysVendor),
                Candidates = Candidates(x.Id.Value, catalogue),
            })
            .ToList();
    }

    internal readonly record struct PciId(string Vendor, string Device, string SubsysDevice, string SubsysVendor, string? Revision)
    {
        public string Key => $@"PCI\VEN_{Vendor}&DEV_{Device}&SUBSYS_{SubsysDevice}{SubsysVendor}";
    }

    internal static PciId? Parse(Inventory.LocalDevice d)
    {
        foreach (var id in d.HardwareIds)
        {
            var m = PciIdRegex().Match(id);
            if (m.Success)
            {
                var g = m.Groups;
                return new PciId(g[1].Value.ToUpperInvariant(), g[2].Value.ToUpperInvariant(), g[3].Value.ToUpperInvariant(),
                    g[4].Value.ToUpperInvariant(), g[5].Success ? g[5].Value.ToUpperInvariant() : null);
            }
        }
        return null;
    }

    [GeneratedRegex(@"^PCI\\VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})&SUBSYS_([0-9A-F]{4})([0-9A-F]{4})(?:&REV_([0-9A-F]{2}))?", RegexOptions.IgnoreCase)]
    private static partial Regex PciIdRegex();

    /// <summary>
    /// The maker's cards on this GPU. An entry with a revision ("1002:7550:C0") must match it: AMD tells the RX 9070 XT
    /// from the GRE by revision alone. Cards whose subsystem IDs the index lists either match exactly or are ruled out.
    /// </summary>
    internal static IReadOnlyList<Board> Candidates(PciId id, IEnumerable<Board> cards)
    {
        var device = $"{id.Vendor}:{id.Device}";
        var withRevision = id.Revision is null ? null : $"{device}:{id.Revision}";
        var onGpu = cards
            .Where(b => string.Equals(b.Pci?.SubsysVendor, id.SubsysVendor, StringComparison.OrdinalIgnoreCase)
                        && b.Pci!.Device?.Any(d => d.Equals(device, StringComparison.OrdinalIgnoreCase)
                                                   || d.Equals(withRevision, StringComparison.OrdinalIgnoreCase)) == true)
            .ToList();

        var exact = onGpu.Where(b => b.Pci!.Subsys?.Contains(id.SubsysDevice, StringComparer.OrdinalIgnoreCase) == true).ToList();
        return exact.Count > 0 ? exact : onGpu.Where(b => b.Pci!.Subsys is not { Count: > 0 }).ToList();
    }
}

/// <summary>A graphics card's catalogue entries and their reports, and the one shown when it can be told.</summary>
public sealed record CardAssessment
{
    public required GraphicsCard Card { get; init; }

    /// <summary>The user's choice, or the only candidate; null when several could be this card.</summary>
    public Board? Selected { get; init; }

    public bool IsOverride { get; init; }

    /// <summary>Reports of <see cref="GraphicsCard.Candidates"/> (or of the chosen card) that loaded.</summary>
    public IReadOnlyList<BoardAssessment> Reports { get; init; } = [];

    public string? Error { get; init; }

    public BoardAssessment? SelectedReport => Selected is null ? null : Reports.FirstOrDefault(r => r.Report.Board.BoardId == Selected.BoardId);

    /// <summary>
    /// Asking for the exact model only pays where a model lists a VBIOS: otherwise the maker's models on one chip
    /// list the same driver, or differ only in how recently their page was updated.
    /// </summary>
    public bool ModelMatters => Selected is null && Reports.Any(r => r.Report.Bios is not null);

    /// <summary>The selected model's report, else the candidates' (for the listing they share).</summary>
    public IReadOnlyList<BoardAssessment> Shown => SelectedReport is { } s ? [s] : Reports;
}
