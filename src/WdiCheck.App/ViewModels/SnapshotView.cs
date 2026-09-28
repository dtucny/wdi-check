using WdiCheck.Core.Api;
using WdiCheck.Core.Matching;
using WdiCheck.Core.Scanning;

namespace WdiCheck.App.ViewModels;

public sealed record SummaryTile(string Count, string Label, Severity Severity, string Filter);

public sealed record BoardFamilyItem(
    string Family, string Component, string Listed, string ListedDate, string Newest, string NewestDate,
    string Installed, string LagText, Severity LagSeverity, bool UpstreamOnly, string SameLineNote);

/// <summary>Everything the pages display, computed once per scan (or board change).</summary>
public sealed class SnapshotView
{
    public SnapshotView(ScanSnapshot s)
    {
        Snapshot = s;
        var boardVendor = s.SelectedBoard?.Vendor;
        Rows = s.Results
            .Select(r => new DeviceRowViewModel(r, boardVendor))
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

        Attention = Rows.Where(r => r.Result.Status == DriverStatus.Behind).Take(8).ToList();
        var matched = Count(r => r.Status is DriverStatus.Current or DriverStatus.Behind);
        Headline = Attention.Count == 0
            ? matched == 0 ? "No indexed drivers found on this PC." : $"All {matched} comparable drivers are current."
            : $"{Count(r => r.Status == DriverStatus.Behind)} of {matched} comparable drivers have a newer release.";
        HeadlineSeverity = farBehind > 0 ? Severity.Critical : Attention.Count > 0 ? Severity.Warning : Severity.Good;

        IdentityText = s.Identity.DisplayName;
        var f = s.Freshness;
        FreshnessText = $"Index generated {f.IndexGenerated?[..Math.Min(10, f.IndexGenerated.Length)] ?? "unknown"} · downloaded {Display.DateTime(f.FetchedAt)}";
        IsStale = f.IsStale;
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
        if (b is not null)
        {
            VendorBiosText = b.VendorLastBios is { } vb ? Display.Date(vb) : "not listed";
            (BiosPill, BiosSeverity) = b.BiosStatus switch
            {
                BiosStatus.Current => ("Current", Severity.Good),
                BiosStatus.Behind => (Display.Lag(b.BiosLagDays), Display.LagSeverity(b.BiosLagDays)),
                _ => ("Unknown", Severity.Neutral),
            };
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
    }

    public ScanSnapshot Snapshot { get; }
    public IReadOnlyList<DeviceRowViewModel> Rows { get; }
    public IReadOnlyList<SummaryTile> Tiles { get; }
    public IReadOnlyList<DeviceRowViewModel> Attention { get; }
    public bool HasAttention => Attention.Count > 0;
    public string Headline { get; }
    public Severity HeadlineSeverity { get; }
    public string IdentityText { get; }
    public string FreshnessText { get; }
    public bool IsStale { get; }
    public string StaleText { get; }

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
