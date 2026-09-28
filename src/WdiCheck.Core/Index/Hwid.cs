using System.Text.RegularExpressions;
using WdiCheck.Core.Inventory;

namespace WdiCheck.Core.Index;

public static partial class Hwid
{
    public static string Normalise(string? hwid) => (hwid ?? "").Trim().ToUpperInvariant();

    /// <summary>
    /// Class-level IDs that identify a kind of device rather than a device: PCI class codes
    /// (<c>PCI\CC_010802</c>, <c>PCI\VEN_1022&amp;CC_0104</c>), USB class IDs, bare HDAUDIO
    /// function IDs. Matching on these pairs inbox Microsoft drivers with unrelated vendor
    /// families (standard NVMe → "AMD RAID"), so they are ignored on both sides.
    /// </summary>
    public static bool IsGeneric(string normalisedHwid) => GenericRegex().IsMatch(normalisedHwid);

    /// <summary>INF string-key parsing leftovers in 1.x data, e.g. <c>PCI\AMDPCIE.DEVICEDESC</c>, <c>ACPI\PARAMETERS</c>.</summary>
    public static bool IsMalformed(string normalisedHwid) =>
        !normalisedHwid.Contains('\\')
        || normalisedHwid.EndsWith(".DEVICEDESC", StringComparison.Ordinal)
        || normalisedHwid == @"ACPI\PARAMETERS";

    /// <summary>The ID without SUBSYS/REV qualifiers — the form infs.json publishes.</summary>
    public static string Strip(string normalisedHwid) => QualifierRegex().Replace(normalisedHwid, "");

    /// <summary>
    /// A device's hardware then compatible IDs, most specific first, each followed by its
    /// SUBSYS/REV-stripped form; normalised, de-duplicated, class-level IDs removed.
    /// </summary>
    public static IReadOnlyList<string> DeviceIds(LocalDevice device)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in device.HardwareIds.Concat(device.CompatibleIds))
        {
            var id = Normalise(raw);
            foreach (var candidate in new[] { id, Strip(id) })
            {
                if (candidate.Length > 0 && !IsGeneric(candidate) && seen.Add(candidate))
                    result.Add(candidate);
            }
        }

        return result;
    }

    [GeneratedRegex(@"^(PCI\\(VEN_[0-9A-F]{4}&)?CC_|USB\\CLASS_|USB\\COMPOSITE$|USB\\DEVCLASS_|HDAUDIO\\FUNC_\d+$|\*)")]
    private static partial Regex GenericRegex();

    [GeneratedRegex(@"&(SUBSYS_[0-9A-F]+|REV_[0-9A-F]+)")]
    private static partial Regex QualifierRegex();
}
