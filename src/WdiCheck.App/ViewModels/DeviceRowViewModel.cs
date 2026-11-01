using WdiCheck.Core.Inventory;
using WdiCheck.Core.Matching;
using WdiCheck.Core.Reporting;

namespace WdiCheck.App.ViewModels;

public sealed record HwidItem(string Id, bool IsMatch, string Kind);

public sealed record KnownVersionItem(string Version, string Date, string Sources, bool IsInstalled);

public sealed record LinkItem(string Title, string Detail, string Url, bool IsBoardVendor);

/// <summary>A driver against the listing of the product it belongs to: "MSI", "board" (or "laptop", "card").</summary>
public sealed record VendorListing(VendorComparison Comparison, string Vendor, string Product);

/// <summary>One row in the Drivers list, plus everything the detail pane shows.</summary>
public sealed class DeviceRowViewModel
{
    public DeviceRowViewModel(DeviceResult r, string? boardVendor, WindowsUpdateInfo? windowsUpdate = null, SystemIdentity? identity = null,
        VendorListing? vendor = null)
    {
        Result = r;
        var d = r.Device;
        Name = d.Name;
        Family = r.Family?.Name ?? d.Class ?? "";
        Subtitle = r.Family is not null ? r.Family.Name : string.Join(" · ", new[] { d.Class, d.DriverProvider }.Where(s => !string.IsNullOrEmpty(s)));
        Installed = d.DriverVersion ?? "—";
        InstalledCanonical = r.Installed is { } iv && !string.Equals(iv.Normalised, d.DriverVersion, StringComparison.OrdinalIgnoreCase)
                             && iv.Raw != d.DriverVersion
            ? $"= {iv.Raw} in this family's numbering"
            : "";
        InstalledDate = Display.Date(r.InstalledDate);
        LineNewest = r.Newest?.Raw ?? "—";
        LineNewestDate = Display.Date(r.Newest?.Date);
        NewestSource = r.Inf is { } im
            ? $"newest release of {im.Newest.InfName ?? "this INF"}"
            : r.Installed?.Line is { } l && r.Family is { } f ? $"newest {f.Name} release on the {l} line" : "";
        if (r.UpdateOnlyFromCatalog)
            NewestSource += (NewestSource.Length > 0 ? "; " : "") + "a newer build exists (another OEM's, via Windows Update)";
        Newest = (r.Update ?? r.Newest)?.Raw ?? "—";
        FamilyNewest = r.Family?.Water?.Version ?? "—";
        FamilyNewestDate = Display.Date(r.Family?.WaterDate);
        FamilyNewestSources = r.Family?.Water is { } w
            ? string.Join(", ", (w.PublishedBy ?? []).Select(Display.Vendor)) + (w.UpstreamOnly ? " (upstream only)" : "")
            : "";
        // The family's newest is a package version (AMD's chipset package 7.12.30.211); this driver an INF in it (1.0.0.12).
        if (r.Family is { Water: not null } fw && r.Installed?.Line is { } il && fw.Effective(fw.Water.Version).Line is { } wl && wl != il)
            FamilyNewestSources += " · a package version, numbered differently from this driver";
        (StatusText, Severity) = Display.Status(r);
        GroupKey = r.Status switch
        {
            DriverStatus.NotIndexed => "~notindexed",
            _ => r.Component,
        };
        GroupName = r.Status == DriverStatus.NotIndexed ? "Not in the index" : Display.Component(r.Component);
        Reason = r.Reason;
        Provider = d.DriverProvider ?? "";
        Inf = d.OriginalInfName is { } orig && !string.Equals(orig, d.InfPath, StringComparison.OrdinalIgnoreCase)
            ? $"{orig} (installed as {d.InfPath})"
            : d.InfPath ?? "";
        ClassName = d.Class ?? "";
        DriverDate = d.DriverDate is { } dd ? Display.Date(DateOnly.FromDateTime(dd.UtcDateTime)) : "";
        ConfidenceText = r.Confidence switch
        {
            MatchConfidence.InfExact => "Compared INF-to-INF: your exact driver version is in the index.",
            MatchConfidence.Inf => "Compared INF-to-INF: your exact driver version isn't in the index.",
            MatchConfidence.ExactVersion => "Your exact version is listed in the index.",
            MatchConfidence.SameLine => "Your exact version isn't listed; compared against versions on the same numbering line.",
            _ => "",
        };

        Ids = d.HardwareIds.Select(id => new HwidItem(id, id == r.MatchedId, "Hardware ID"))
            .Concat(d.CompatibleIds.Select(id => new HwidItem(id, id == r.MatchedId, "Compatible ID")))
            .ToList();

        OtherFamilies = string.Join(", ", r.OtherCandidates.Select(f => f.Name));

        Links = DownloadLinks.For(r.Target, boardVendor)
            .Take(6)
            .Select(l => new LinkItem(
                $"{Display.Vendor(l.Vendor)} · {(l.Contains is null ? "" : "package ")}{l.Version}",
                string.Join(" · ", new[] { l.IsBoardVendor ? "Your board vendor" : null, l.ReleaseDate,
                    l.Contains is null ? null : $"contains {l.Contains}" }.Where(x => !string.IsNullOrEmpty(x))),
                l.Url.AbsoluteUri,
                l.IsBoardVendor))
            .ToList();

        if (DownloadLinks.Hint(r) is { } hint)
        {
            HintLabel = hint.Label;
            HintUrl = hint.Url!;
        }

        ParallelNote = r.ParallelLine is { } p
            ? $"The family's newest release, {p.Version} ({Display.Date(p.FirstPublished)}), is on a different numbering line. " +
              "That can be a newer driver model for the same hardware, or a different component of the same package."
            : "";

        LineVersions = r.Inf is { } inf
            ? inf.Releases
                .Take(12)
                .Select(v => new KnownVersionItem(
                    v.Row.DriverVer,
                    Display.Date(v.DriverDate),
                    string.Join(", ", new[] { v.InfName }.Concat(v.Artefacts.Select(a => Display.Vendor(a.Vendor)).Distinct()).Where(x => !string.IsNullOrEmpty(x))),
                    r.Installed is not null && v.Version.Equals(r.Installed)))
                .ToList()
            : r.Installed?.Line is { } line && r.Family is { } fam
            ? fam.Versions
                .Where(v => v.Version.Line == line && !v.IsBeta)
                .Take(12)
                .Select(v => new KnownVersionItem(
                    v.Version.Raw,
                    Display.Date(v.FirstReleased),
                    string.Join(", ", v.Vendors.Select(Display.Vendor)),
                    r.Installed is not null && v.Version.Equals(r.Installed)))
                .ToList()
            : [];

        // What Windows Update offers this exact device: machine-targeted, unlike "newest anywhere".
        var wu = windowsUpdate ?? WindowsUpdateInfo.Off;
        var offer = wu.OfferFor(d);
        var sameAsBios = offer is not null && identity is not null && offer.MatchesInstalledBios(identity);
        HasWindowsUpdateUpdate = offer is not null && !sameAsBios;
        WindowsUpdateOfferIsOptional = offer?.BrowseOnly == true;
        WindowsUpdateButtonText = WindowsUpdateOfferIsOptional ? "Open optional updates" : "Open Windows Update";
        WindowsUpdateText = wu.Status switch
        {
            WindowsUpdateStatus.Searching when r.Status != DriverStatus.NotIndexed => "Checking Windows Update…",
            WindowsUpdateStatus.Done when sameAsBios =>
                $"Windows Update offers firmware package {offer!.Version?.Raw ?? offer.Title}, dated {Display.Date(offer.DriverDate)}: the same date as " +
                $"the installed BIOS ({identity!.BiosVersion}). It's most likely the BIOS you already have, packaged for Windows Update, and is offered " +
                "because this device only has Microsoft's generic firmware driver. Installing it shouldn't change the firmware.",
            WindowsUpdateStatus.Done when offer is not null =>
                $"Windows Update offers {offer.Version?.Raw ?? offer.Title}" +
                (offer.DriverDate is { } od ? $" (dated {Display.Date(od)})" : "") +
                (offer.BrowseOnly ? " as an optional update." : ". Windows installs it automatically."),
            WindowsUpdateStatus.Done when r.UpdateOnlyFromCatalog =>
                "Windows Update offers no driver for this device. The newer build is from the Windows Update Catalog: Windows Update " +
                "offers it to some OEM's machines, but not to this one.",
            WindowsUpdateStatus.Done when r.Status is DriverStatus.Behind =>
                "Windows Update offers no driver for this device: the newer build isn't targeted at this machine through Windows Update. Get it from the vendor.",
            WindowsUpdateStatus.Done when r.Status is not DriverStatus.NotIndexed => "Windows Update offers no driver for this device.",
            _ => "",
        };

        // Behind the vendor (an update the vendor supports) or the vendor behind the newest (an update from elsewhere).
        Vendor = vendor;
        IsBehindVendor = vendor?.Comparison.Standing == VendorStanding.Behind;
        if (vendor is { Comparison: var vc, Vendor: var v, Product: var product })
        {
            (VendorPill, VendorSeverity) = Display.Standing(vc, v);
            if (vc.Listed is { } listed)
            {
                VendorLatest = $"{v}'s latest: {listed}" + (vc.ListedDate is { } ld ? $" ({ld})" : "");
                VendorLine = VendorLatest +
                             (vc.VendorVersion is { } build && !WdiCheck.Core.Versions.DriverVersion.Parse(build).Equals(WdiCheck.Core.Versions.DriverVersion.Parse(listed))
                                 ? $", with this driver at {build}"
                                 : "");
            }
            VendorText = vc.Standing switch
            {
                VendorStanding.Behind =>
                    $"{v}'s listing for this {product} has a newer build, {vc.VendorVersion}" +
                    (vc.LagDays is { } lag ? $" ({lag} days newer)" : "") + $". This is an update {v} supports: get it from {v}'s support page.",
                VendorStanding.Same when r.Status == DriverStatus.Behind =>
                    $"This is the build {v} lists for this {product}. The newer build isn't on {v}'s listing; it comes from the chip vendor or another manufacturer.",
                VendorStanding.Same => $"This is the build {v} lists for this {product}.",
                VendorStanding.Newer when r.Status == DriverStatus.Behind =>
                    $"Newer than the build {v} lists for this {product} ({vc.VendorVersion}), and a newer build still exists elsewhere.",
                VendorStanding.Newer => $"Newer than the build {v} lists for this {product} ({vc.VendorVersion}).",
                _ => "",
            };
        }

        if (HasWindowsUpdateUpdate && r.Status != DriverStatus.Behind)
        {
            AttentionDetail = $"{(string.IsNullOrEmpty(Provider) ? Family : Provider)} · {Installed} → {offer!.Version?.Raw ?? "newer"} from Windows Update";
            AttentionPill = "Windows Update";
            AttentionSeverity = Severity.Warning;
        }
        else
        {
            AttentionDetail = $"{Family} · {Installed} → {Newest}";
            AttentionPill = PairedStatusText;
            AttentionSeverity = Severity;
        }

        SortKey = r.Status switch
        {
            DriverStatus.Behind => 0,
            _ when HasWindowsUpdateUpdate => 1,
            DriverStatus.Current => 2,
            DriverStatus.NotComparable => 3,
            DriverStatus.Inbox => 4,
            _ => 5,
        };
    }

    public DeviceResult Result { get; }
    public string Name { get; }
    public string Family { get; }
    public string Subtitle { get; }
    public string Installed { get; }
    public string InstalledCanonical { get; }
    public string InstalledDate { get; }
    public string LineNewest { get; }
    public string LineNewestDate { get; }

    /// <summary>What <see cref="LineNewest"/> is: the newest release of the INF, or of the family's version line.</summary>
    public string NewestSource { get; }

    /// <summary>What the list shows: the update target when behind, else the newest on the installed line.</summary>
    public string Newest { get; }
    public string FamilyNewest { get; }
    public string FamilyNewestDate { get; }
    public string FamilyNewestSources { get; }
    public string StatusText { get; }
    public Severity Severity { get; }
    public string GroupKey { get; }
    public string GroupName { get; }
    public string Reason { get; }
    public string Provider { get; }
    public string Inf { get; }
    public string ClassName { get; }
    public string DriverDate { get; }
    public string ConfidenceText { get; }
    public IReadOnlyList<HwidItem> Ids { get; }
    public string OtherFamilies { get; }
    public IReadOnlyList<LinkItem> Links { get; }
    public string ParallelNote { get; }
    public IReadOnlyList<KnownVersionItem> LineVersions { get; }
    public int SortKey { get; }

    /// <summary>The family's generic driver source (e.g. NVIDIA App), when the index names one.</summary>
    public string HintLabel { get; } = "";
    public string HintUrl { get; } = "";

    public bool HasHint => HintUrl.Length > 0;
    public bool HasLinks => Links.Count > 0;

    /// <summary>What Windows Update offers this device, or that it offers nothing (empty while off or not run).</summary>
    public string WindowsUpdateText { get; } = "";

    public bool HasWindowsUpdateText => WindowsUpdateText.Length > 0;

    /// <summary>Windows Update offers this device a driver it doesn't have (not a repackaged copy of the installed BIOS).</summary>
    public bool HasWindowsUpdateUpdate { get; }

    public bool WindowsUpdateOfferIsOptional { get; }
    public string WindowsUpdateButtonText { get; }

    /// <summary>The "Needs attention" line: family · installed → newest, or the Windows Update offer.</summary>
    public string AttentionDetail { get; }
    public string AttentionPill { get; }
    public Severity AttentionSeverity { get; }
    /// <summary>The installed driver against its board's or card's vendor listing (empty when it can't be compared).</summary>
    public string VendorPill { get; } = "";
    public Severity VendorSeverity { get; } = Severity.Neutral;
    public string VendorText { get; } = "";
    public bool HasVendorText => VendorText.Length > 0;
    public bool HasVendorPill => VendorPill.Length > 0;

    /// <summary>The status next to the vendor pill: "186 d behind newest", so the two lags can't be confused.</summary>
    public string PairedStatusText => HasVendorPill && Result.Status == DriverStatus.Behind ? $"{StatusText} newest" : StatusText;

    /// <summary>The board's or card's listing this driver was compared with; null when there is none.</summary>
    public VendorListing? Vendor { get; }

    /// <summary>"MSI's latest: 8.03.25.247 (2026-09-18)": the vendor's listing, whether or not it compares.</summary>
    public string VendorLatest { get; } = "";

    /// <summary><see cref="VendorLatest"/>, plus this driver's build in the vendor's package when that's numbered differently.</summary>
    public string VendorLine { get; } = "";
    public bool HasVendorLine => VendorLine.Length > 0;

    /// <summary>The vendor lists a newer build: the update it supports, listed first under "Needs attention".</summary>
    public bool IsBehindVendor { get; }

    public bool HasUpdateSources => HasLinks || HasHint;

    /// <summary>Date · source, without a dangling separator when either is missing (INF releases without a DriverVer date).</summary>
    public string LineNewestDetail => string.Join(" · ", new[] { LineNewestDate, NewestSource }.Where(x => x.Length > 0));
    public string FamilyNewestDetail => string.Join(" · ", new[] { FamilyNewestDate, FamilyNewestSources }.Where(x => x.Length > 0));

    /// <summary>"Get the update" only when there is one; a current driver gets where its family comes from.</summary>
    public string UpdateSourcesHeader => Result.Status == DriverStatus.Behind ? "Get the update" : "Where to get it";
    public bool HasParallelNote => ParallelNote.Length > 0;
    public bool HasLineVersions => LineVersions.Count > 0;
    public bool HasOtherFamilies => OtherFamilies.Length > 0;
    public bool HasFamily => Result.Family is not null;

    /// <summary>Accessible name of the list item (ListView uses ToString for UI Automation).</summary>
    public override string ToString() => $"{Name}, {StatusText}";

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query)
        || Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Family.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Provider.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Installed.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Ids.Any(i => i.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
}

public sealed class DeviceGroup(string key, string header, IEnumerable<DeviceRowViewModel> items) : List<DeviceRowViewModel>(items)
{
    public string Key { get; } = key;
    public string Header { get; } = header;
    public string CountText => Count == 1 ? "1 device" : $"{Count} devices";

    public override string ToString() => $"{Header}, {CountText}";
}
