using System.Globalization;
using WdiCheck.Core.Api;
using WdiCheck.Core.Inventory;

namespace WdiCheck.Core.Matching;

public enum BiosStatus
{
    Unknown,
    /// <summary>Installed BIOS is dated on/after the vendor's newest listed BIOS.</summary>
    Current,
    /// <summary>The vendor lists a BIOS newer than the installed one.</summary>
    Behind,
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

    public static BoardAssessment Create(BoardReport report, SystemIdentity identity, IReadOnlyList<DeviceResult> devices)
    {
        var last = DateOnly.TryParseExact(report.Bios?.LastBios, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : (DateOnly?)null;
        var status = BiosStatus.Unknown;
        int? lag = null;
        if (last is { } l && identity.BiosReleaseDate is { } mine)
        {
            status = mine >= l ? BiosStatus.Current : BiosStatus.Behind;
            lag = Math.Max(0, l.DayNumber - mine.DayNumber);
        }

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
