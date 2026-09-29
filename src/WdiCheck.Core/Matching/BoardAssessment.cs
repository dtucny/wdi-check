using System.Globalization;
using WdiCheck.Core.Api;
using WdiCheck.Core.Inventory;

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

/// <summary>A by-board family row joined with what this PC actually has installed.</summary>
public sealed record BoardFamilyComparison(BoardFamilyRow Row, IReadOnlyList<DeviceResult> Installed)
{
    public string InstalledSummary => Installed.Count == 0
        ? "—"
        : string.Join(", ", Installed.Select(r => r.Device.DriverVersion).Where(v => v is not null).Distinct());
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

    public static BoardAssessment Create(BoardReport report, SystemIdentity identity, IReadOnlyList<DeviceResult> devices)
    {
        // The versioned BIOS's own publish date when the index gives one, else the newest BIOS date.
        var dateText = !string.IsNullOrEmpty(report.Bios?.LastBiosVersionDate) ? report.Bios.LastBiosVersionDate : report.Bios?.LastBios;
        var last = DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : (DateOnly?)null;
        var (status, lag) = AssessBios(report.Bios, last, identity);

        var byFamily = devices
            .Where(r => r.Family is not null && r.Status is not DriverStatus.Inbox and not DriverStatus.NotIndexed)
            .GroupBy(r => r.Family!.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DeviceResult>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        return new BoardAssessment
        {
            Report = report,
            Identity = identity,
            BiosStatus = status,
            VendorLastBios = last,
            BiosLagDays = lag,
            Families = report.Families
                .Select(f => new BoardFamilyComparison(f, byFamily.GetValueOrDefault(f.Family) ?? []))
                .ToList(),
        };
    }
}
