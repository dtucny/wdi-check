using System.Text.RegularExpressions;

namespace WdiCheck.Core.Versions;

/// <summary>
/// Per-family translations from a listing/INF numbering scheme to the canonical scheme
/// the family's water level uses. Mirrors <c>SCHEME_EQUIV</c> in WinDriverIndex's
/// <c>publish.py</c>; the index does not yet publish these rules as data
/// (see doc/upstream-requests.md), so they are duplicated here.
/// </summary>
public static partial class SchemeEquivalence
{
    public const string NvidiaGraphics = "NVIDIA Graphics";
    public const string RealtekAudio = "Realtek Audio";

    /// <summary>Canonical form of <paramref name="raw"/> for the family, or null if no rule applies.</summary>
    public static string? ToCanonical(string familyName, string? raw) => familyName switch
    {
        NvidiaGraphics => NvidiaMarketing(raw),
        RealtekAudio => RealtekUad(raw),
        _ => null,
    };

    /// <summary>Parse <paramref name="raw"/> after applying the family's scheme translation.</summary>
    public static DriverVersion Effective(string familyName, string? raw) =>
        DriverVersion.Parse(ToCanonical(familyName, raw) ?? raw);

    /// <summary>
    /// NVIDIA stamps Windows packages with the INF DriverVer (3x.0.1D.DDDD) while NVIDIA itself
    /// speaks the marketing scheme: 32.0.15.9186 → 591.86 (last digit of the third field + the fourth).
    /// Intel's 32.0.101.xxxx has a three-digit third field and never matches.
    /// </summary>
    public static string? NvidiaMarketing(string? raw)
    {
        var m = NvInfRegex().Match((raw ?? "").Trim());
        return m.Success ? $"{m.Groups[1].Value}{m.Groups[2].Value[..2]}.{m.Groups[2].Value[2..]}" : null;
    }

    /// <summary>ASRock writes Realtek UAD audio as the bare build ('10007.1_UAD_WHQL'); canonical is 6.0.&lt;build&gt;.&lt;rev&gt;.</summary>
    public static string? RealtekUad(string? raw)
    {
        var m = RtkUadRegex().Match((raw ?? "").Trim());
        return m.Success ? $"6.0.{m.Groups[1].Value}.{m.Groups[2].Value}" : null;
    }

    [GeneratedRegex(@"^3\d\.0\.1(\d)\.(\d{4})\s*$")]
    private static partial Regex NvInfRegex();

    [GeneratedRegex(@"^(\d{4,5})\.(\d+)[_ ]?UAD", RegexOptions.IgnoreCase)]
    private static partial Regex RtkUadRegex();
}
