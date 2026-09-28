using System.Text.RegularExpressions;

namespace WdiCheck.Core.Versions;

/// <summary>
/// The comparable form of a driver version string — a port of WinDriverIndex's
/// <c>winidx/versions.py</c> (spec §6.3). The longest dotted numeric run wins, so
/// 'XB560NF_v6001.16.175.0' and '6.0.9520.1_Nahimic' both parse; segments compare
/// numerically so '3.05.00.1380' equals '3.5.0.1380'. The raw string is always kept.
/// </summary>
public sealed partial class DriverVersion : IComparable<DriverVersion>, IEquatable<DriverVersion>
{
    private DriverVersion(long[]? parts, bool isBeta, string raw)
    {
        Parts = parts;
        IsBeta = isBeta;
        Raw = raw;
    }

    /// <summary>Numeric segments, or null when the string has no dotted numeric run.</summary>
    public IReadOnlyList<long>? Parts { get; }

    public bool IsBeta { get; }

    public string Raw { get; }

    public bool IsParsed => Parts is not null;

    public long? Major => Parts?[0];

    /// <summary>
    /// The numbering line this version belongs to. Vendors publish the same family in
    /// incompatible schemes (package vs INF versions, NDIS vs NetAdapter lines), so
    /// versions are only compared within a line. Two-part versions are marketing numbers
    /// (NVIDIA 591.86 -> 616.92) whose leading part is a release branch, so they form one line.
    /// </summary>
    public VersionLine? Line => Parts is null ? null
        : Parts.Count <= 2 ? new VersionLine(VersionLine.AnyMajor, Parts.Count)
        : new VersionLine(Parts[0], Parts.Count);

    /// <summary>Canonical dotted form without zero padding, e.g. "3.5.0.1380".</summary>
    public string Normalised => Parts is null ? Raw : string.Join('.', Parts);

    public static DriverVersion Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new DriverVersion(null, false, raw ?? "");

        var isBeta = BetaRegex().IsMatch(raw);
        string? best = null;
        foreach (Match m in DottedRegex().Matches(raw))
        {
            // max(runs, key=len): longest string wins, ties go to the earliest.
            if (best is null || m.Value.Length > best.Length)
                best = m.Value;
        }

        if (best is null)
            return new DriverVersion(null, isBeta, raw);

        var segments = best.Split('.');
        var parts = new long[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!long.TryParse(segments[i], out parts[i]))
                return new DriverVersion(null, isBeta, raw);
        }

        return new DriverVersion(parts, isBeta, raw);
    }

    /// <summary>Unparseable versions sort lowest; mixed lengths compare as if zero-padded.</summary>
    public int CompareTo(DriverVersion? other)
    {
        if (other is null)
            return 1;
        if (Parts is null || other.Parts is null)
            return (Parts is null ? 0 : 1) - (other.Parts is null ? 0 : 1);

        var n = Math.Max(Parts.Count, other.Parts.Count);
        for (var i = 0; i < n; i++)
        {
            var a = i < Parts.Count ? Parts[i] : 0;
            var b = i < other.Parts.Count ? other.Parts[i] : 0;
            if (a != b)
                return a.CompareTo(b);
        }

        return 0;
    }

    public bool Equals(DriverVersion? other) =>
        other is not null && (Parts is null || other.Parts is null
            ? Parts is null && other.Parts is null && string.Equals(Raw, other.Raw, StringComparison.OrdinalIgnoreCase)
            : CompareTo(other) == 0);

    public override bool Equals(object? obj) => obj is DriverVersion v && Equals(v);

    public override int GetHashCode()
    {
        if (Parts is null)
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Raw);
        // Trailing zeros are insignificant for equality, so they must not affect the hash.
        var last = Parts.Count - 1;
        while (last > 0 && Parts[last] == 0)
            last--;
        var h = new HashCode();
        for (var i = 0; i <= last; i++)
            h.Add(Parts[i]);
        return h.ToHashCode();
    }

    public override string ToString() => Raw;

    public static bool operator <(DriverVersion a, DriverVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(DriverVersion a, DriverVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(DriverVersion a, DriverVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(DriverVersion a, DriverVersion b) => a.CompareTo(b) >= 0;

    [GeneratedRegex(@"\d+(?:\.\d+)+")]
    private static partial Regex DottedRegex();

    [GeneratedRegex("beta", RegexOptions.IgnoreCase)]
    private static partial Regex BetaRegex();
}

/// <summary>A version numbering line: leading segment plus segment count.</summary>
public readonly record struct VersionLine(long Major, int Segments)
{
    /// <summary>Major used for two-part (marketing) versions, which form a single line.</summary>
    public const long AnyMajor = -1;

    public override string ToString() => Major == AnyMajor ? $"{Segments}-part" : $"{Major}.x";
}
