using WdiCheck.Core.Matching;
using WdiCheck.Core.Reporting;

namespace WdiCheck.App.ViewModels;

public sealed record HwidItem(string Id, bool IsMatch, string Kind);

public sealed record KnownVersionItem(string Version, string Date, string Sources, bool IsInstalled);

public sealed record LinkItem(string Title, string Detail, string Url, bool IsBoardVendor);

/// <summary>One row in the Drivers list, plus everything the detail pane shows.</summary>
public sealed class DeviceRowViewModel
{
    public DeviceRowViewModel(DeviceResult r, string? boardVendor)
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
        LineNewest = r.LineNewest?.Version.Raw ?? "—";
        Newest = (r.Update ?? r.LineNewest)?.Version.Raw ?? "—";
        LineNewestDate = Display.Date(r.LineNewest?.FirstReleased);
        FamilyNewest = r.Family?.Water?.Version ?? "—";
        FamilyNewestDate = Display.Date(r.Family?.WaterDate);
        FamilyNewestSources = r.Family?.Water is { } w
            ? string.Join(", ", (w.PublishedBy ?? []).Select(Display.Vendor)) + (w.UpstreamOnly ? " (upstream only)" : "")
            : "";
        (StatusText, Severity) = Display.Status(r);
        GroupKey = r.Status switch
        {
            DriverStatus.NotIndexed => "~notindexed",
            _ => r.Component,
        };
        GroupName = r.Status == DriverStatus.NotIndexed ? "Not in the index" : Display.Component(r.Component);
        Reason = r.Reason;
        Provider = d.DriverProvider ?? "";
        Inf = d.InfPath ?? "";
        ClassName = d.Class ?? "";
        DriverDate = d.DriverDate is { } dd ? Display.Date(DateOnly.FromDateTime(dd.UtcDateTime)) : "";
        ConfidenceText = r.Confidence switch
        {
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
                $"{Display.Vendor(l.Vendor)} · {l.Version}",
                (l.IsBoardVendor ? "Your board vendor · " : "") + (l.ReleaseDate ?? ""),
                l.Url.AbsoluteUri,
                l.IsBoardVendor))
            .ToList();

        ParallelNote = r.ParallelLine is { } p
            ? $"The family's newest release, {p.Version} ({Display.Date(p.FirstPublished)}), is on a different numbering line. " +
              "That can be a newer driver model for the same hardware, or a different component of the same package."
            : "";

        LineVersions = r.Installed?.Line is { } line && r.Family is { } fam
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

        SortKey = r.Status switch
        {
            DriverStatus.Behind => 0,
            DriverStatus.Current => 1,
            DriverStatus.NotComparable => 2,
            DriverStatus.Inbox => 3,
            _ => 4,
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

    public bool HasLinks => Links.Count > 0;
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
