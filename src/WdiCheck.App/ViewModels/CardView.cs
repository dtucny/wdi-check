using System.Text.RegularExpressions;
using WdiCheck.Core.Api;
using WdiCheck.Core.Matching;

namespace WdiCheck.App.ViewModels;

/// <summary>What a graphics card's page shows: the card as far as it can be told, its VBIOS and the maker's listing.</summary>
public sealed partial class CardView
{
    public CardView(CardAssessment a)
    {
        Assessment = a;
        var card = a.Card;
        Key = card.Key;
        var maker = card.Maker ?? "Add-in";
        Maker = maker;
        NavTitle = $"{maker} {ShortGpuName(card.Gpu.Device.Name)}";
        var count = card.Candidates.Count;
        if (a.Selected is { } s)
        {
            Title = $"{Display.Vendor(s.Vendor)} {s.Name}";
            Subtitle = card.Gpu.Device.Name;
            MatchText = a.IsOverride ? "Chosen by you" : $"The only {maker} card on this GPU in the catalogue";
            SupportUrl = s.SupportUrl is { } u && Uri.TryCreate(u, UriKind.Absolute, out var su) && su.Scheme == Uri.UriSchemeHttps ? su : null;
        }
        else
        {
            Title = $"{maker} {card.Gpu.Device.Name}";
            Subtitle = count == 0
                ? $"This card isn't in the WinDriverIndex catalogue."
                : $"One of {count} {maker} models on this GPU in the catalogue";
            MatchText = count == 0 ? "" : "Identified by the GPU and the card maker's PCI IDs, which a maker's models on one GPU share";
        }
        IdText = card.Key;
        Candidates = card.Candidates;
        CandidatesHeader = count == 1 ? "1 model in the catalogue" : $"Models this could be ({count})";
        Error = a.Error ?? "";

        // Installed driver: the GPU's own status, in the family's numbering where it has one (NVIDIA 32.0.16.2101 = 621.01).
        var gpu = card.Gpu;
        var installed = gpu.Device.DriverVersion ?? "—";
        InstalledText = gpu.Family?.Effective(gpu.Device.DriverVersion) is { IsParsed: true } iv && iv.Raw != installed
            ? $"{installed} (= {iv.Raw})"
            : installed;
        (StatusText, StatusSeverity) = Display.Status(gpu);
        NewestText = gpu.Status == DriverStatus.Behind && gpu.Target is { } t
            ? $"Newest: {t.Raw}" + (t.Date is { } d ? $" ({Display.Date(d)})" : "")
            : "";

        // VBIOS: per model, so only shown for a known model; otherwise say whether picking one would show it.
        if (a.SelectedReport?.Report.Bios is { } bios)
        {
            VbiosText = string.IsNullOrWhiteSpace(bios.LastBiosVersion)
                ? Display.Date(bios.LastBios)
                : bios.LastBiosVersionDate is { } vd ? $"{bios.LastBiosVersion} ({vd})" : bios.LastBiosVersion;
        }
        else if (a.ModelMatters)
        {
            var listing = a.Reports.Count(r => r.Report.Bios is not null);
            VbiosNote = $"{listing} of these models list a VBIOS on {maker}'s site. Pick your model below to see its newest.";
        }

        var vendor = a.Shown.FirstOrDefault() is { } first ? Display.Vendor(first.Report.Board.Vendor) : maker;
        Vendor = vendor;
        Families = a.Shown
            .SelectMany(r => r.Families)
            .GroupBy(f => f.Row.Family, StringComparer.OrdinalIgnoreCase)
            .Select(g => BoardFamilyItem.From(g.ToList(), vendor))
            .ToList();

        // MSI's card pages link to NVIDIA's driver rather than listing one of their own.
        if (Families.Count == 0 && a.Shown.Select(r => r.Report.GpuReference).FirstOrDefault(g => g?.WaterVersion is not null) is { } reference)
        {
            var chip = Display.SiliconVendor(gpu.Family?.Family.SiliconVendor) ?? "the GPU maker";
            ReferenceText = $"{vendor} doesn't list a driver of its own for this card; its support page links to {chip}'s. " +
                            $"{chip}'s newest: {reference.WaterVersion}" +
                            (reference.WaterFirstPublished is { } p ? $" ({p})." : ".");
        }
    }

    public CardAssessment Assessment { get; }

    /// <summary>The card's PCI ID, which the page and the user's choice of model are keyed by.</summary>
    public string Key { get; }

    public string Maker { get; }

    /// <summary>"MSI RTX 4070 Ti": short enough for the navigation pane.</summary>
    public string NavTitle { get; }

    public string Title { get; }
    public string Subtitle { get; }
    public string MatchText { get; }
    public string IdText { get; }
    public Uri? SupportUrl { get; }
    public bool HasSupportUrl => SupportUrl is not null;
    public bool IsOverride => Assessment.IsOverride;

    public IReadOnlyList<Board> Candidates { get; }
    public string CandidatesHeader { get; }
    public bool HasCandidates => Candidates.Count > 1 || (Candidates.Count == 1 && IsOverride);

    /// <summary>A candidate lists a VBIOS, so the exact model would change the page: ask for it.</summary>
    public bool ModelMatters => Assessment.ModelMatters;

    public string Error { get; }
    public bool HasError => Error.Length > 0;

    public string InstalledText { get; }
    public string StatusText { get; }
    public Severity StatusSeverity { get; }
    public string NewestText { get; }
    public bool HasNewest => NewestText.Length > 0;

    public string VbiosText { get; } = "";
    public bool HasVbios => VbiosText.Length > 0;
    public string VbiosNote { get; } = "";
    public bool HasVbiosNote => VbiosNote.Length > 0;

    /// <summary>"MSI": the listing's column header.</summary>
    public string Vendor { get; }

    public IReadOnlyList<BoardFamilyItem> Families { get; }
    public bool HasFamilies => Families.Count > 0;
    public string ReferenceText { get; } = "";
    public bool HasReference => ReferenceText.Length > 0;

    /// <summary>"NVIDIA GeForce RTX 4070 Ti" → "RTX 4070 Ti"; "AMD Radeon(TM) RX 9070 XT" → "RX 9070 XT".</summary>
    internal static string ShortGpuName(string name) =>
        Regex.Replace(BrandWords().Replace(name, ""), @"\s+", " ").Trim() is { Length: > 0 } s ? s : name;

    [GeneratedRegex(@"\b(NVIDIA|GeForce|AMD|Radeon|Intel|Arc)\b|\((TM|R)\)|™|®", RegexOptions.IgnoreCase)]
    private static partial Regex BrandWords();
}
