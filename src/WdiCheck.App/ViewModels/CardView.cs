using System.Text.RegularExpressions;
using WdiCheck.Core.Api;
using WdiCheck.Core.Matching;

namespace WdiCheck.App.ViewModels;

/// <summary>A model the card could be, with its pages: the maker's support page and its WinDriverIndex report.</summary>
public sealed record CardCandidate(Board Board, Uri? SupportUrl, Uri? IndexPageUrl)
{
    public bool HasSupportUrl => SupportUrl is not null;
    public bool HasIndexPageUrl => IndexPageUrl is not null;
}

/// <summary>What a graphics card's page shows: the card as far as it can be told, its VBIOS and the maker's listing.</summary>
public sealed partial class CardView
{
    public CardView(CardAssessment a, Uri? dataSource = null)
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
            SupportUrl = Https(s.SupportUrl);
            IndexPageUrl = dataSource is null ? null : WdiClient.BoardPage(dataSource, s.BoardId);
        }
        else
        {
            // No one report for "one of 20": the site's search, where the models can be compared.
            IndexPageUrl = dataSource is null ? null : WdiClient.BoardSearch(dataSource);
            IndexPageText = "Search WinDriverIndex";
            IndexPageTip = $"Search the WinDriverIndex catalogue for this card's models (e.g. “{ShortGpuName(card.Gpu.Device.Name)}”)";
            Title = $"{maker} {card.Gpu.Device.Name}";
            Subtitle = count == 0
                ? $"This card isn't in the WinDriverIndex catalogue."
                : $"One of {count} {maker} models on this GPU in the catalogue";
            MatchText = count == 0 ? "" : "Identified by the GPU and the card maker's PCI IDs, which a maker's models on one GPU share";
        }
        IdText = card.Key;
        Candidates = card.Candidates.Select(b => new CardCandidate(b, Https(b.SupportUrl),
            dataSource is null ? null : WdiClient.BoardPage(dataSource, b.BoardId))).ToList();
        CandidatesHeader = a.IsOverride
            ? $"Change your model ({count} match these PCI IDs)"
            : $"Pick your model ({count} match these PCI IDs)";
        // Without a prompt, say why: the models share what this page shows, so picking only adds the support page.
        if (a.Selected is null && count > 1 && !a.ModelMatters)
            ModelNote = "Which of these you have doesn't change this page: none lists a VBIOS, and their driver listings are the same. " +
                        "Pick yours to link its support page.";
        Error = a.Error ?? "";

        // Installed driver: the GPU's own status, in the family's numbering where it has one (NVIDIA 32.0.16.2101 = 621.01).
        var gpu = card.Gpu;
        var installed = gpu.Device.DriverVersion ?? "—";
        InstalledText = gpu.Family?.Effective(gpu.Device.DriverVersion) is { IsParsed: true } iv && iv.Raw != installed
            ? $"{installed} (= {iv.Raw})"
            : installed;
        (StatusText, StatusSeverity) = Display.Status(gpu);
        // The GPU maker's own driver page (NVIDIA's for any maker's RTX card), else the family's download hint.
        if (gpu.Family is { } gpuFamily)
        {
            SiliconUrl = MachineHardware.NewestListedBySilicon(gpuFamily, gpu.Device.DriverVersion)?.Url
                         ?? Https(Core.Reporting.DownloadLinks.Hint(gpu)?.Url);
            SiliconLinkText = $"Get from {Display.SiliconVendor(gpuFamily.Family.SiliconVendor) ?? "the GPU maker"}";
        }
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
    /// <summary>The chosen model's report on the WinDriverIndex site; without one, the site's search.</summary>
    public Uri? IndexPageUrl { get; }
    public bool HasIndexPageUrl => IndexPageUrl is not null;
    public string IndexPageText { get; } = "WinDriverIndex page";
    public string IndexPageTip { get; } = "This model's report on the WinDriverIndex site";

    /// <summary>"Get from NVIDIA": the GPU maker's driver page, whichever card maker built the card.</summary>
    public Uri? SiliconUrl { get; }
    public string SiliconLinkText { get; } = "";
    public bool HasSiliconUrl => SiliconUrl is not null;
    public bool IsOverride => Assessment.IsOverride;

    public IReadOnlyList<CardCandidate> Candidates { get; }
    public string CandidatesHeader { get; }
    public bool HasCandidates => Candidates.Count > 1 || (Candidates.Count == 1 && IsOverride);

    /// <summary>A candidate lists a VBIOS, so the exact model would change the page: ask for it.</summary>
    public bool ModelMatters => Assessment.ModelMatters;

    /// <summary>Why the page doesn't ask for the model, when it doesn't.</summary>
    public string ModelNote { get; } = "";
    public bool HasModelNote => ModelNote.Length > 0;

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

    private static Uri? Https(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u : null;

    /// <summary>"NVIDIA GeForce RTX 4070 Ti" → "RTX 4070 Ti"; "AMD Radeon(TM) RX 9070 XT" → "RX 9070 XT".</summary>
    internal static string ShortGpuName(string name) =>
        Regex.Replace(BrandWords().Replace(name, ""), @"\s+", " ").Trim() is { Length: > 0 } s ? s : name;

    [GeneratedRegex(@"\b(NVIDIA|GeForce|AMD|Radeon|Intel|Arc)\b|\((TM|R)\)|™|®", RegexOptions.IgnoreCase)]
    private static partial Regex BrandWords();
}
