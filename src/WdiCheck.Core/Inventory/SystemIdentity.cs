using System.Globalization;
using Microsoft.Win32;

namespace WdiCheck.Core.Inventory;

/// <summary>SMBIOS identity of this machine, as Windows mirrors it into the registry.</summary>
public sealed record SystemIdentity
{
    public string? BaseBoardManufacturer { get; init; }
    public string? BaseBoardProduct { get; init; }
    public string? BaseBoardVersion { get; init; }
    public string? SystemManufacturer { get; init; }
    public string? SystemProductName { get; init; }
    public string? SystemFamily { get; init; }
    public string? SystemVersion { get; init; }
    public string? SystemSku { get; init; }
    public string? BiosVendor { get; init; }
    public string? BiosVersion { get; init; }
    public DateOnly? BiosReleaseDate { get; init; }

    /// <summary>The CPU's brand string, e.g. "AMD Ryzen 7 7800X3D 8-Core Processor".</summary>
    public string? ProcessorName { get; init; }

    /// <summary>
    /// ESRT firmware resources of type "system firmware", i.e. the BIOS itself (HKLM\HARDWARE\UEFI\ESRT).
    /// Empty on legacy BIOS or when unreadable.
    /// </summary>
    public IReadOnlyList<Guid> SystemFirmwareResources { get; init; } = [];

    public string DisplayName
    {
        get
        {
            // DIY boards repeat the board code as the system product ("MS-7D73" inside
            // "MPG B650I EDGE WIFI (MS-7D73)"), so only show the system separately when it adds something.
            var systemAddsInfo = !IsPlaceholder(SystemProductName)
                                 && !(BaseBoardProduct ?? "").Contains(SystemProductName!, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(BaseBoardProduct) && systemAddsInfo)
                return $"{SystemManufacturer} {SystemProductName} · board {BaseBoardProduct}";
            return $"{BaseBoardManufacturer ?? SystemManufacturer} {BaseBoardProduct ?? SystemProductName}".Trim();
        }
    }

    /// <summary>OEM placeholder strings DIY boards leave in system fields.</summary>
    public static bool IsPlaceholder(string? s) =>
        string.IsNullOrWhiteSpace(s)
        || s.Contains("To be filled", StringComparison.OrdinalIgnoreCase)
        || s.Contains("System Product Name", StringComparison.OrdinalIgnoreCase)
        || s.Contains("System manufacturer", StringComparison.OrdinalIgnoreCase)
        || s.Equals("Default string", StringComparison.OrdinalIgnoreCase)
        || s.Equals("O.E.M.", StringComparison.OrdinalIgnoreCase);

    public static SystemIdentity ReadFromRegistry()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        string? Get(string name) => key?.GetValue(name) is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

        return new SystemIdentity
        {
            BaseBoardManufacturer = Get("BaseBoardManufacturer"),
            BaseBoardProduct = Get("BaseBoardProduct"),
            BaseBoardVersion = Get("BaseBoardVersion"),
            SystemManufacturer = Get("SystemManufacturer"),
            SystemProductName = Get("SystemProductName"),
            SystemFamily = Get("SystemFamily"),
            SystemVersion = Get("SystemVersion"),
            SystemSku = Get("SystemSKU"),
            BiosVendor = Get("BIOSVendor"),
            BiosVersion = Get("BIOSVersion"),
            // SMBIOS mandates mm/dd/yyyy.
            BiosReleaseDate = DateOnly.TryParseExact(Get("BIOSReleaseDate"), "MM/dd/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d) ? d : null,
            SystemFirmwareResources = ReadSystemFirmwareResources(),
            ProcessorName = ReadProcessorName(),
        };
    }

    private static string? ReadProcessorName()
    {
        using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return cpu?.GetValue("ProcessorNameString") is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
    }

    // ESRT entry Type: 1 = system firmware, 2 = device firmware, 3 = UEFI driver. Readable without elevation.
    private static IReadOnlyList<Guid> ReadSystemFirmwareResources()
    {
        try
        {
            using var esrt = Registry.LocalMachine.OpenSubKey(@"HARDWARE\UEFI\ESRT");
            if (esrt is null)
                return [];
            var list = new List<Guid>();
            foreach (var name in esrt.GetSubKeyNames())
            {
                using var entry = esrt.OpenSubKey(name);
                if (entry?.GetValue("Type") is int type && type == 1 && Guid.TryParse(name, out var guid))
                    list.Add(guid);
            }

            return list;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
