using System.Text.RegularExpressions;

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

    /// <summary>INF string-key parsing leftovers in the index, e.g. <c>PCI\AMDPCIE.DEVICEDESC</c>, <c>ACPI\PARAMETERS</c>.</summary>
    public static bool IsMalformed(string normalisedHwid) =>
        !normalisedHwid.Contains('\\')
        || normalisedHwid.EndsWith(".DEVICEDESC", StringComparison.Ordinal)
        || normalisedHwid == @"ACPI\PARAMETERS";

    [GeneratedRegex(@"^(PCI\\(VEN_[0-9A-F]{4}&)?CC_|USB\\CLASS_|USB\\COMPOSITE$|USB\\DEVCLASS_|HDAUDIO\\FUNC_\d+$|\*)")]
    private static partial Regex GenericRegex();
}
