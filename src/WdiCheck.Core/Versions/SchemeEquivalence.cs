using System.Text.RegularExpressions;
using WdiCheck.Core.Api;

namespace WdiCheck.Core.Versions;

/// <summary>
/// Per-family translations from a listing/INF numbering scheme to the canonical scheme the
/// family's water level uses. Schema 2.0.0 publishes these as <c>families.version_equiv</c>;
/// the built-in rules below mirror them for 1.x data that lacks the field.
/// </summary>
public static partial class SchemeEquivalence
{
    public const string NvidiaGraphics = "NVIDIA Graphics";
    public const string RealtekAudio = "Realtek Audio";

    /// <summary>The family's translation: its published rule, else the built-in one, else null.</summary>
    public static Func<string?, string?>? For(Family family) =>
        family.VersionEquiv is { } rule ? FromRule(rule) : BuiltIn(family.Name);

    /// <summary>Compiles a published rule; an invalid pattern yields no translation rather than an error.</summary>
    public static Func<string?, string?>? FromRule(VersionEquivRule rule)
    {
        if (string.IsNullOrEmpty(rule.Pattern))
            return null;
        Regex rx;
        try
        {
            var options = RegexOptions.CultureInvariant;
            if (rule.Flags?.Contains('i') == true)
                options |= RegexOptions.IgnoreCase;
            rx = new Regex(rule.Pattern, options, TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            return null;
        }

        // Match.Result keeps only the rewritten text, dropping suffixes the pattern didn't consume ("_WHQL_Nahimic").
        return raw =>
        {
            try
            {
                var m = rx.Match((raw ?? "").Trim());
                return m.Success ? m.Result(rule.Replace) : null;
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        };
    }

    private static Func<string?, string?>? BuiltIn(string familyName) => familyName switch
    {
        NvidiaGraphics => NvidiaMarketing,
        RealtekAudio => RealtekUad,
        _ => null,
    };

    /// <summary>Canonical form of <paramref name="raw"/> using the built-in rule for the family, or null.</summary>
    public static string? ToCanonical(string familyName, string? raw) => BuiltIn(familyName)?.Invoke(raw);

    /// <summary>Parse <paramref name="raw"/> after applying the family's built-in scheme translation.</summary>
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
