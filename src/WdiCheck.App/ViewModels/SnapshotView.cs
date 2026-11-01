using WdiCheck.Core.Api;
using WdiCheck.Core.Matching;
using WdiCheck.Core.Scanning;
using WdiCheck.Core.WindowsUpdate;

namespace WdiCheck.App.ViewModels;

public sealed record SummaryTile(string Count, string Label, Severity Severity, string Filter);

public sealed record BoardFamilyItem(
    string Family, string Component, string Listed, string ListedDate, string Newest, string NewestDate,
    string Installed, string LagText, Severity LagSeverity, bool UpstreamOnly, string SameLineNote);

/// <summary>Everything the pages display, computed once per scan (or board change).</summary>
public sealed partial class SnapshotView
{
    public SnapshotView(ScanSnapshot s, WindowsUpdateInfo? windowsUpdate = null)
    {
        Snapshot = s;
        WindowsUpdate = windowsUpdate ?? WindowsUpdateInfo.Off;
        var boardVendor = s.SelectedBoard?.Vendor;
        Rows = s.Results
            .Select(r => new DeviceRowViewModel(r, boardVendor, WindowsUpdate, s.Identity))
            .OrderBy(r => r.SortKey)
            .ThenByDescending(r => r.Result.LagDays ?? 0)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var results = s.Results;
        int Count(Func<DeviceResult, bool> f) => results.Count(f);
        var farBehind = Count(r => r.Status == DriverStatus.Behind && r.LagDays > 365);
        var behind = Count(r => r.Status == DriverStatus.Behind) - farBehind;
        Tiles =
        [
            new(Count(r => r.Status == DriverStatus.Current).ToString(), "Current", Severity.Good, Filters.Current),
            new(behind.ToString(), "Behind ≤ 1 yr", behind > 0 ? Severity.Warning : Severity.Neutral, Filters.BehindNear),
            new(farBehind.ToString(), "Over 1 yr behind", farBehind > 0 ? Severity.Critical : Severity.Neutral, Filters.BehindFar),
            new(Count(r => r.Status == DriverStatus.NotComparable).ToString(), "Not comparable", Severity.Neutral, Filters.NotComparable),
            new(Count(r => r.Status == DriverStatus.Inbox).ToString(), "Windows driver", Severity.Neutral, Filters.Inbox),
            new(Count(r => r.Status == DriverStatus.NotIndexed).ToString(), "Not in index", Severity.Neutral, Filters.NotIndexed),
        ];

        // Behind first (sorted by lag), then devices Windows Update has an update for that the index doesn't flag.
        Attention = Rows.Where(r => r.Result.Status == DriverStatus.Behind).Take(8)
            .Concat(Rows.Where(r => r.HasWindowsUpdateUpdate && r.Result.Status != DriverStatus.Behind))
            .ToList();
        WindowsUpdateUpdates = WindowsUpdate.Offers.Where(o => !o.MatchesInstalledBios(s.Identity)).ToList();
        WindowsUpdateSameAsBios = WindowsUpdate.Offers.Where(o => o.MatchesInstalledBios(s.Identity)).ToList();
        var matched = Count(r => r.Status is DriverStatus.Current or DriverStatus.Behind);
        var behindCount = Count(r => r.Status == DriverStatus.Behind);
        Headline = behindCount == 0
            ? matched == 0 ? "No indexed drivers found on this PC." : $"All {matched} comparable drivers are current."
            : $"{Count(r => r.Status == DriverStatus.Behind)} of {matched} comparable drivers have a newer release.";
        HeadlineSeverity = farBehind > 0 ? Severity.Critical : behindCount > 0 ? Severity.Warning : Severity.Good;

        IdentityText = s.Identity.DisplayName;
        var f = s.Freshness;
        FreshnessText = $"Index generated {f.IndexGenerated?[..Math.Min(10, f.IndexGenerated.Length)] ?? "unknown"} · downloaded {Display.DateTime(f.FetchedAt)}";
        IsStale = f.IsStale;
        SchemaWarning = f.IsNewerSchema
            ? $"WinDriverIndex now publishes data schema {f.SchemaVersion}; this version of WdiCheck understands up to {DataFreshness.SupportedSchemaMajor}.x. Results may be incomplete — check for a WdiCheck update."
            : !f.HasInfData
                ? "This data source has no INF-level data (infs.json), so drivers are compared by package version line only; comparisons are less precise."
                : "";
        StaleText = f.IsStale ? $"Couldn't reach WinDriverIndex ({f.Error}). Showing the copy downloaded {Display.DateTime(f.FetchedAt)}." : "";

        // Board
        var b = s.Board;
        HasBoard = s.SelectedBoard is not null;
        BoardTitle = s.SelectedBoard is { } sb ? $"{Display.Vendor(sb.Vendor)} {sb.Name}" : "Machine not identified";
        BoardSubtitle = s.SelectedBoard is { } sb2
            ? string.Join(" · ", new[] { sb2.ProductType, sb2.Chipset, sb2.Socket }.Where(x => !string.IsNullOrEmpty(x)))
            : s.BoardCandidates.Count > 0
                ? "Several catalogue entries could match — pick yours on the board page."
                : "This machine's model isn't in the WinDriverIndex catalogue (MSI, Gigabyte, ASRock, ASUS, Lenovo, Dell, HP).";
        BoardMatchText = s.SelectedBoard is null
            ? ""
            : s.BoardIsOverride
                ? "Chosen by you"
                : s.BoardCandidates.FirstOrDefault(c => c.Board.BoardId == s.SelectedBoard.BoardId)?.Reason is { } reason
                    ? $"Matched from SMBIOS {reason}"
                    : "";
        SupportUrl = s.SelectedBoard?.SupportUrl is { } u && Uri.TryCreate(u, UriKind.Absolute, out var su) && su.Scheme == Uri.UriSchemeHttps ? su : null;
        BoardError = s.BoardError ?? "";

        var installedBios = $"{s.Identity.BiosVersion ?? "unknown"}{(s.Identity.BiosReleaseDate is { } bd ? $" ({Display.Date(bd)})" : "")}";
        InstalledBiosText = installedBios;
        BiosSummaryText = installedBios;
        if (b is not null)
        {
            var vendorVersion = b.Report.Bios?.LastBiosVersion;
            var vendorDate = b.VendorLastBios is { } vb ? Display.Date(vb) : null;
            VendorBiosText = string.IsNullOrWhiteSpace(vendorVersion)
                ? vendorDate ?? "not listed"
                : vendorDate is null ? vendorVersion : $"{vendorVersion} ({vendorDate})";

            // Shown like a driver update: installed → newest, in the headline, "Needs attention" and with a link.
            BiosUpdateAvailable = b.BiosStatus == BiosStatus.Behind;
            if (BiosUpdateAvailable)
            {
                var target = string.IsNullOrWhiteSpace(vendorVersion) ? $"newer BIOS ({vendorDate})" : vendorVersion;
                BiosSummaryText = $"{s.Identity.BiosVersion ?? "installed"} → {target}";
                BiosUpdateText = $"{Display.Vendor(b.Report.Board.Vendor)} {b.Report.Board.Name} · {BiosSummaryText}";
                Headline += string.IsNullOrWhiteSpace(vendorVersion)
                    ? " The vendor also lists a newer BIOS."
                    : $" The vendor also lists a newer BIOS, {vendorVersion}.";
                if (HeadlineSeverity == Severity.Good)
                    HeadlineSeverity = Severity.Warning;
            }
            (BiosPill, BiosSeverity) = b.BiosStatus switch
            {
                BiosStatus.Current => ("Current", Severity.Good),
                BiosStatus.Behind => (Display.Lag(b.BiosLagDays), Display.LagSeverity(b.BiosLagDays)),
                BiosStatus.Unconfirmed => ("Probably current", Severity.Neutral),
                _ => ("Unknown", Severity.Neutral),
            };
            BiosNote = b.BiosStatus == BiosStatus.Unconfirmed
                ? $"The vendor's newest BIOS was published {b.BiosLagDays} days after your BIOS's build date. Vendors usually publish a " +
                  $"BIOS weeks to months after building it, so this is likely the same release; the index lists only dates, not versions. " +
                  $"Check that {s.Identity.BiosVersion ?? "your version"} is the newest on the vendor's support page."
                : "";
            AgesaText = b.Report.Bios?.Agesa is { } ag
                ? $"Vendor's newest BIOS ships AGESA {ag} ({b.Report.Bios.AgesaLine})" + (b.Report.Bios.AgesaAtWater == true ? " — the newest AMD has released." : " — AMD has released newer.")
                : "";
            var worst = b.WorstListingLag;
            (VendorVerdict, VendorVerdictSeverity) = worst switch
            {
                null => ($"{Display.Vendor(b.Report.Board.Vendor)} lists no comparable drivers for this model.", Severity.Neutral),
                0 => ($"{Display.Vendor(b.Report.Board.Vendor)}'s listing is fully current — every driver family is at the newest release.", Severity.Good),
                <= 365 => ($"{Display.Vendor(b.Report.Board.Vendor)}'s listing is reasonably maintained — worst family is {worst} days behind.", Severity.Warning),
                _ => ($"{b.Report.Families.Count(x => x.LagDays > 365)} of {b.Report.Families.Count} driver families on {Display.Vendor(b.Report.Board.Vendor)}'s listing are more than a year behind — worst is {Display.Lag(worst)}.", Severity.Critical),
            };
            BoardFamilies = b.Families.Select(c => new BoardFamilyItem(
                c.Row.Family,
                Display.Component(c.Row.Component) + (c.Row.UpstreamOnly ? " · newest ships upstream only" : ""),
                (c.Row.ListedVersion ?? "—") + (c.Row.ListedEquiv is { } eq ? $" (= {eq})" : ""),
                c.Row.ListedDate ?? "",
                c.Row.WaterVersion ?? "—",
                c.Row.WaterFirstPublished ?? "",
                c.InstalledSummary,
                c.Row.LagDays is null ? "n/a" : Display.Lag(c.Row.LagDays),
                c.Row.LagDays is null ? Severity.Neutral : Display.LagSeverity(c.Row.LagDays),
                c.Row.UpstreamOnly,
                c.Row.SameLineNewest is { } sl && sl != c.Row.ListedVersion ? $"Newest on the listed line: {sl}" : "")).ToList();
        }

        Candidates = s.BoardCandidates;
        ShowCandidates = s.SelectedBoard is null && s.BoardCandidates.Count > 0;

        // Hardware as the drivers relate to it: processor and GPUs always, everything else on request.
        var rowFor = Rows.ToDictionary(r => r.Result);
        var machine = MachineHardware.Build(s.Identity, s.Results, s.Infs);
        var hardware = machine
            .Select(h => (h.IsCore, Row: new HardwareRowViewModel(h, h.Driver is { } d ? rowFor.GetValueOrDefault(d) : null)))
            .ToList();
        CoreHardware = hardware.Where(h => h.IsCore).Select(h => h.Row).ToList();
        MoreHardware = hardware.Where(h => !h.IsCore).Select(h => h.Row).ToList();

        // Overview header: the board, then the CPU and any GPU that isn't built in (an iGPU's name adds nothing).
        var cpu = s.Identity.ProcessorName is { } p ? CoreCountSuffix().Replace(p, "") : null;
        var cards = machine
            .Where(h => h.Component == "graphics" && h.Detail != "built-in")
            .Select(h => h.Name);
        MachineText = string.Join(" · ", new[] { IdentityText, cpu }.Concat(cards).Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    /// <summary>"…8-Core Processor" on AMD brand strings, which the header can do without.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\s+\d+-Core Processor\s*$")]
    private static partial System.Text.RegularExpressions.Regex CoreCountSuffix();

    /// <summary>The Overview header: board identity, CPU and add-in GPUs.</summary>
    public string MachineText { get; }

    public IReadOnlyList<HardwareRowViewModel> CoreHardware { get; }
    public IReadOnlyList<HardwareRowViewModel> MoreHardware { get; }
    public bool HasCoreHardware => CoreHardware.Count > 0;
    public bool HasMoreHardware => MoreHardware.Count > 0;
    public string MoreHardwareHeader => MoreHardware.Count == 1 ? "1 more component" : $"All components ({MoreHardware.Count} more)";

    public ScanSnapshot Snapshot { get; }

    public WindowsUpdateInfo WindowsUpdate { get; }

    public bool ShowWindowsUpdate => WindowsUpdate.Status != WindowsUpdateStatus.Off;

    public bool IsSearchingWindowsUpdate => WindowsUpdate.Status == WindowsUpdateStatus.Searching;

    /// <summary>Offers that would change something (excludes a repackaged copy of the installed BIOS).</summary>
    public IReadOnlyList<WindowsUpdateOffer> WindowsUpdateUpdates { get; }

    /// <summary>System-firmware packages dated the same day as the installed BIOS.</summary>
    public IReadOnlyList<WindowsUpdateOffer> WindowsUpdateSameAsBios { get; }

    public bool HasWindowsUpdateUpdates => WindowsUpdateUpdates.Count > 0;

    /// <summary>Every real offer is optional, so they're listed under Optional updates rather than on the main page.</summary>
    public bool WindowsUpdateOnlyOptional => HasWindowsUpdateUpdates && WindowsUpdateUpdates.All(o => o.BrowseOnly);

    public string WindowsUpdateButtonText => WindowsUpdateOnlyOptional ? "Open optional updates" : "Open Windows Update";

    /// <summary>One line on the Overview: what Windows Update offers this exact machine.</summary>
    public string WindowsUpdateText => WindowsUpdate.Status switch
    {
        WindowsUpdateStatus.Searching => "Checking Windows Update for drivers it offers this PC…",
        WindowsUpdateStatus.Done when WindowsUpdateUpdates.Count == 0 =>
            "Windows Update offers no driver updates for this PC." +
            (WindowsUpdateSameAsBios.Count > 0 ? " The firmware package it lists matches the installed BIOS (below)." : "") +
            " Newer builds the index lists come from other vendors' or models' packages, which Windows Update doesn't target at this machine.",
        WindowsUpdateStatus.Done => (WindowsUpdateUpdates.Count == 1
                ? "Windows Update offers 1 driver update for this PC."
                : $"Windows Update offers {WindowsUpdateUpdates.Count} driver updates for this PC.") +
            (WindowsUpdateUpdates.Any(o => !o.BrowseOnly) ? " Windows installs those that aren't optional automatically." : ""),
        WindowsUpdateStatus.Failed => $"Couldn't check Windows Update: {WindowsUpdate.Result?.Error}",
        _ => "",
    };

    public Severity WindowsUpdateSeverity => WindowsUpdate.Status switch
    {
        WindowsUpdateStatus.Done => WindowsUpdateUpdates.Count == 0 ? Severity.Good : Severity.Warning,
        _ => Severity.Neutral,
    };

    /// <summary>Each offer as "Title (date) · how it installs"; BIOS repackages explained.</summary>
    public IReadOnlyList<string> WindowsUpdateOfferLines => WindowsUpdateUpdates
        .Select(o => OfferLine(o) + (o.BrowseOnly ? " · optional" : " · installs automatically"))
        .Concat(WindowsUpdateSameAsBios.Select(o =>
            OfferLine(o) + $" · same date as the installed BIOS ({Snapshot.Identity.BiosVersion}): most likely the BIOS you already have"))
        .ToList();

    private static string OfferLine(WindowsUpdateOffer o) => o.Title + (o.DriverDate is { } d ? $" ({Display.Date(d)})" : "");
    public IReadOnlyList<DeviceRowViewModel> Rows { get; }
    public IReadOnlyList<SummaryTile> Tiles { get; }
    public IReadOnlyList<DeviceRowViewModel> Attention { get; }
    public bool HasAttention => Attention.Count > 0 || BiosUpdateAvailable;

    /// <summary>The vendor lists a newer BIOS than the installed one.</summary>
    public bool BiosUpdateAvailable { get; }

    /// <summary>"1.L2 (2026-06-28)", or "1.L2 → 7D73v1L3" when an update is available.</summary>
    public string BiosSummaryText { get; } = "";

    /// <summary>The "Needs attention" line for a BIOS update: board and installed → newest.</summary>
    public string BiosUpdateText { get; } = "";

    public bool HasBiosUpdateLink => BiosUpdateAvailable && SupportUrl is not null;

    public string BiosUpdateLinkText => Snapshot.Board is { } b
        ? $"{Display.Vendor(b.Report.Board.Vendor)} · {(string.IsNullOrWhiteSpace(b.Report.Bios?.LastBiosVersion) ? "newest BIOS" : $"BIOS {b.Report.Bios.LastBiosVersion}")}"
        : "";
    public string Headline { get; }
    public Severity HeadlineSeverity { get; }
    public string IdentityText { get; }
    public string FreshnessText { get; }
    public bool IsStale { get; }
    public string StaleText { get; }
    public string SchemaWarning { get; } = "";
    public bool HasSchemaWarning => SchemaWarning.Length > 0;

    public bool HasBoard { get; }
    public bool HasBoardReport => Snapshot.Board is not null;
    public string BoardTitle { get; }
    public string BoardSubtitle { get; }
    public string BoardMatchText { get; }
    public Uri? SupportUrl { get; }
    public bool HasSupportUrl => SupportUrl is not null;
    public string BoardError { get; }
    public bool HasBoardError => BoardError.Length > 0;
    public string InstalledBiosText { get; }
    public string VendorBiosText { get; } = "";
    public string BiosNote { get; } = "";
    public bool HasBiosNote => BiosNote.Length > 0;
    public string BiosPill { get; } = "Unknown";
    public Severity BiosSeverity { get; } = Severity.Neutral;
    public string AgesaText { get; } = "";
    public string VendorVerdict { get; } = "";
    public Severity VendorVerdictSeverity { get; } = Severity.Neutral;
    public IReadOnlyList<BoardFamilyItem> BoardFamilies { get; } = [];
    public IReadOnlyList<BoardCandidate> Candidates { get; }
    public bool ShowCandidates { get; }
    public bool IsBoardOverride => Snapshot.BoardIsOverride;
}

public static class Filters
{
    public const string AllMatched = "matched";
    public const string Behind = "behind";
    public const string BehindNear = "behind-near";
    public const string BehindFar = "behind-far";
    public const string Current = "current";
    public const string NotComparable = "notcomparable";
    public const string Inbox = "inbox";
    public const string NotIndexed = "notindexed";
    public const string Everything = "all";

    public static bool Accept(string filter, DeviceResult r) => filter switch
    {
        Behind => r.Status == DriverStatus.Behind,
        BehindNear => r.Status == DriverStatus.Behind && r.LagDays is null or <= 365,
        BehindFar => r.Status == DriverStatus.Behind && r.LagDays > 365,
        Current => r.Status == DriverStatus.Current,
        NotComparable => r.Status == DriverStatus.NotComparable,
        Inbox => r.Status == DriverStatus.Inbox,
        NotIndexed => r.Status == DriverStatus.NotIndexed,
        Everything => true,
        _ => r.Status is not (DriverStatus.Inbox or DriverStatus.NotIndexed),
    };

    /// <summary>Filters that only make sense with in-box/unindexed devices visible.</summary>
    public static bool NeedsAllDevices(string filter) => filter is Inbox or NotIndexed or Everything;
}
