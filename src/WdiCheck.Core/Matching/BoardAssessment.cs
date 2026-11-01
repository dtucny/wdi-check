using System.Globalization;
using WdiCheck.Core.Api;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Versions;

namespace WdiCheck.Core.Matching;

public enum BiosStatus
{
    Unknown,
    /// <summary>Installed BIOS is the vendor's newest (by version), or dated on/after it.</summary>
    Current,
    /// <summary>The vendor lists a BIOS newer than the installed one.</summary>
    Behind,
    /// <summary>
    /// Only dates to go on, and the vendor's newest was published shortly after the installed BIOS was built:
    /// usually the same release (vendors publish weeks to months after the build date SMBIOS reports).
    /// </summary>
    Unconfirmed,
}

public enum VendorStanding
{
    /// <summary>Nothing to compare: no listed version, or numbered differently with no INF from the listed package.</summary>
    Unknown,
    /// <summary>The vendor's listing has a newer build than the installed one.</summary>
    Behind,
    /// <summary>The installed driver is the build the vendor lists.</summary>
    Same,
    /// <summary>The installed driver is newer than the vendor's listing (usually from the chip vendor or another OEM).</summary>
    Newer,
}

/// <summary>An installed driver against the vendor's listing for its family, as opposed to the newest release anywhere.</summary>
/// <param name="VendorVersion">The vendor's build as compared: the INF's DriverVer when matched by INF, else the listed version.</param>
/// <param name="LagDays">When behind and matched by INF: days between the two INFs' DriverVer dates.</param>
public sealed record VendorComparison(VendorStanding Standing, string? VendorVersion = null, int? LagDays = null)
{
    public static VendorComparison Unknown { get; } = new(VendorStanding.Unknown);

    /// <summary>The vendor's latest version as its listing shows it (a package version), and its date.</summary>
    public string? Listed { get; init; }
    public string? ListedDate { get; init; }

    /// <summary>
    /// Compares by INF where the index has the INFs inside the vendor's listed package (the installed INF's own
    /// release in it); otherwise by the listed version, when it's on the installed driver's version line.
    /// </summary>
    public static VendorComparison Of(DeviceResult r, BoardFamilyRow row, string vendor) =>
        Compare(r, row, vendor) with { Listed = row.ListedVersion, ListedDate = row.ListedDate };

    private static VendorComparison Compare(DeviceResult r, BoardFamilyRow row, string vendor)
    {
        if (string.IsNullOrEmpty(row.ListedVersion))
            return Unknown;

        // Inf.Releases are already the releases comparable with this device's driver (same INF or series, binding it).
        if (r.Inf is { } inf
            && inf.Releases
                .Where(x => x.Artefacts.Any(a => a.Vendor.Equals(vendor, StringComparison.OrdinalIgnoreCase) && a.VersionRaw == row.ListedVersion))
                .MaxBy(x => x.Version) is { } listed)
        {
            var installed = inf.Installed?.Version ?? DriverVersion.Parse(r.Device.DriverVersion);
            if (listed.IsBuild(installed))
                return new(VendorStanding.Same, listed.Row.DriverVer);
            if (installed > listed.Version)
                return new(VendorStanding.Newer, listed.Row.DriverVer);
            var lag = r.InstalledDate is { } mine && listed.DriverDate is { } theirs && theirs > mine ? theirs.DayNumber - mine.DayNumber : (int?)null;
            return new(VendorStanding.Behind, listed.Row.DriverVer, lag);
        }

        // By version number: only on the same numbering (NVIDIA's INF version becomes 621.01 through the family's
        // rules). A driver matched by INF also needs the listed version to be a build of that INF: AMD's chipset
        // package 8.03.25.247 shares a line with a component INF's 8.0.0.65 without being a version of it.
        if (r.Family is { } family && family.Effective(r.Device.DriverVersion) is { Line: { } line } mine2)
        {
            foreach (var raw in new[] { row.ListedEquiv, row.ListedVersion })
            {
                if (family.Effective(raw) is not { Line: { } l } theirs || l != line)
                    continue;
                if (r.Inf is { } im && !im.Releases.Any(x => family.Effective(x.Row.DriverVer).Equals(theirs)))
                    continue;
                return theirs.Equals(mine2) ? new(VendorStanding.Same, raw)
                    : mine2 > theirs ? new(VendorStanding.Newer, raw)
                    : new(VendorStanding.Behind, raw);
            }
        }
        return Unknown;
    }

    /// <summary>
    /// Several devices of one family: behind if any is, else newer if any is (AMD: the HDMI audio INF matches MSI's
    /// package, the display driver is newer), else the same; unknown only when none could be compared.
    /// </summary>
    public static VendorComparison Combine(IEnumerable<VendorComparison> all)
    {
        var list = all.ToList();
        var known = list.Where(c => c.Standing != VendorStanding.Unknown).ToList();
        return known.FirstOrDefault(c => c.Standing == VendorStanding.Behind)
               ?? known.FirstOrDefault(c => c.Standing == VendorStanding.Newer)
               ?? known.FirstOrDefault()
               ?? list.FirstOrDefault()
               ?? Unknown;
    }
}

/// <summary>A by-board family row joined with what this PC actually has installed.</summary>
public sealed record BoardFamilyComparison(BoardFamilyRow Row, IReadOnlyList<DeviceResult> Installed)
{
    /// <summary>The family's hardware is present but runs Windows' own driver (the SATA controller on mshdc.inf).</summary>
    public bool UsesWindowsDriver { get; init; }

    /// <summary>
    /// The installed versions; otherwise why there are none: Windows' driver, or no device of the family at all (a
    /// disabled Wi-Fi card, a utility rather than a driver).
    /// </summary>
    public string InstalledSummary => Installed.Count > 0
        ? string.Join(", ", Installed.Select(r => r.Device.DriverVersion).Where(v => v is not null).Distinct())
        : UsesWindowsDriver ? "Windows driver" : "No device found";

    /// <summary>Each installed device against the vendor's listing.</summary>
    public IReadOnlyDictionary<DeviceResult, VendorComparison> ByDevice { get; init; } = new Dictionary<DeviceResult, VendorComparison>();

    /// <summary>The family's installed drivers against the vendor's listing (see <see cref="VendorComparison.Combine"/>).</summary>
    public VendorComparison Vendor => VendorComparison.Combine(ByDevice.Values);
}

public sealed class BoardAssessment
{
    public required BoardReport Report { get; init; }
    public required SystemIdentity Identity { get; init; }
    public BiosStatus BiosStatus { get; init; }
    public DateOnly? VendorLastBios { get; init; }
    public int? BiosLagDays { get; init; }
    public IReadOnlyList<BoardFamilyComparison> Families { get; init; } = [];

    /// <summary>Worst vendor-listing lag across the board's families (the site's headline number).</summary>
    public int? WorstListingLag => Report.Families.Select(f => f.LagDays).Where(l => l is not null).Max();

    /// <summary>
    /// How long after its SMBIOS build date a BIOS may be published and still count as possibly the same release.
    /// Observed: Lenovo M3CN50WW built 2026-04-23, published 2026-08-07 (106 days).
    /// </summary>
    public const int BuildToPublishGraceDays = 180;

    internal static (BiosStatus Status, int? LagDays) AssessBios(BoardBios? vendor, DateOnly? vendorDate, SystemIdentity identity)
    {
        // Version, when the index provides it, beats dates: SMBIOS reports the build date, vendors list the publish date.
        if (!string.IsNullOrWhiteSpace(vendor?.LastBiosVersion) && !string.IsNullOrWhiteSpace(identity.BiosVersion))
        {
            switch (CompareBiosVersions(vendor.LastBiosVersion, identity.BiosVersion))
            {
                case BiosVersionComparison.Same:
                    return (BiosStatus.Current, 0);
                case BiosVersionComparison.Different:
                    return identity.BiosReleaseDate is { } built && vendorDate is { } published && published > built
                        ? (BiosStatus.Behind, published.DayNumber - built.DayNumber)
                        : (BiosStatus.Unknown, null);
                // Incomparable formats: the dates below are all there is to go on.
            }
        }

        if (vendorDate is not { } l || identity.BiosReleaseDate is not { } mine)
            return (BiosStatus.Unknown, null);
        if (mine >= l)
            return (BiosStatus.Current, 0);
        var gap = l.DayNumber - mine.DayNumber;
        return gap <= BuildToPublishGraceDays ? (BiosStatus.Unconfirmed, gap) : (BiosStatus.Behind, gap);
    }

    internal enum BiosVersionComparison { Same, Different, Incomparable }

    /// <summary>
    /// Vendors don't always list a BIOS version the way SMBIOS reports it: MSI's site says <c>7D73v1L3</c> where
    /// Windows reports <c>1.L3</c>; HP's SMBIOS says <c>S70 Ver. 01.29.00</c> where the site lists <c>01.29.00</c>.
    /// Compared on letters and digits only, the shorter against the same-length tail of the longer (3+ characters):
    /// equal is the same version; different but the same shape (digits and letters in the same places:
    /// <c>1l3</c> vs <c>1l2</c>, Lenovo <c>m3cn49ww</c> vs <c>m3cn50ww</c>) is a different version; anything else
    /// is two formats that can't be compared.
    /// </summary>
    internal static BiosVersionComparison CompareBiosVersions(string vendor, string installed)
    {
        var a = Normalise(vendor);
        var b = Normalise(installed);
        if (a.Length == 0 || b.Length == 0)
            return BiosVersionComparison.Incomparable;
        if (a == b)
            return BiosVersionComparison.Same;
        var (longer, shorter) = a.Length >= b.Length ? (a, b) : (b, a);
        if (shorter.Length < 3)
            return BiosVersionComparison.Incomparable;
        var tail = longer[^shorter.Length..];
        if (tail == shorter)
            return BiosVersionComparison.Same;
        return Shape(tail) == Shape(shorter) ? BiosVersionComparison.Different : BiosVersionComparison.Incomparable;

        static string Normalise(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        static string Shape(string s) => new(s.Select(c => char.IsDigit(c) ? '9' : 'a').ToArray());
    }

    /// <summary>The device's driver against this listing; null when the listing has no row for its family.</summary>
    public VendorComparison? ForDevice(DeviceResult r) =>
        Families.Select(f => f.ByDevice.GetValueOrDefault(r)).FirstOrDefault(c => c is not null);

    /// <param name="assessBios">False for a graphics card: the BIOS SMBIOS reports is the board's, not the card's.</param>
    public static BoardAssessment Create(BoardReport report, SystemIdentity identity, IReadOnlyList<DeviceResult> devices, bool assessBios = true)
    {
        // The versioned BIOS's own publish date when the index gives one, else the newest BIOS date.
        var dateText = !string.IsNullOrEmpty(report.Bios?.LastBiosVersionDate) ? report.Bios.LastBiosVersionDate : report.Bios?.LastBios;
        var last = DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : (DateOnly?)null;
        var (status, lag) = assessBios ? AssessBios(report.Bios, last, identity) : (BiosStatus.Unknown, null);

        var byFamily = devices
            .Where(r => r.Family is not null && r.Status is not DriverStatus.Inbox and not DriverStatus.NotIndexed)
            .GroupBy(r => r.Family!.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DeviceResult>)g.ToList(), StringComparer.OrdinalIgnoreCase);
        var onWindowsDriver = devices
            .Where(r => r.Status == DriverStatus.Inbox && r.Family is not null)
            .Select(r => r.Family!.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new BoardAssessment
        {
            Report = report,
            Identity = identity,
            BiosStatus = status,
            VendorLastBios = last,
            BiosLagDays = lag,
            Families = report.Families
                .Select(f =>
                {
                    var installed = byFamily.GetValueOrDefault(f.Family) ?? [];
                    return new BoardFamilyComparison(f, installed)
                    {
                        ByDevice = installed.ToDictionary(r => r, r => VendorComparison.Of(r, f, report.Board.Vendor)),
                        UsesWindowsDriver = onWindowsDriver.Contains(f.Family),
                    };
                })
                .ToList(),
        };
    }
}
