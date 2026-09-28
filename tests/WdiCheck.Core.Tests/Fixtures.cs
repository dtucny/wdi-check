using WdiCheck.Core.Api;
using WdiCheck.Core.Index;
using WdiCheck.Core.Inventory;

namespace WdiCheck.Core.Tests;

/// <summary>
/// A small index built from real WinDriverIndex rows (2026-09-22 publish), trimmed to the
/// families and versions the matching tests exercise. HWID overlaps are kept as published:
/// PCI\VEN_10EC&amp;DEV_8125 really is in four families.
/// </summary>
internal static class Fixtures
{
    private static int _artefactId;

    public static DriverIndex Index() => DriverIndex.Build(Families, Water, Artefacts, "2026-09-22T15:22:43+00:00");

    public static readonly Family[] Families =
    [
        F(7, "Realtek Wi-Fi", "realtek", "wlan", @"PCI\VEN_10EC&DEV_8125", @"PCI\VEN_10EC&DEV_8852"),
        F(87, "Realtek 8168 LAN", "realtek", "lan", @"PCI\VEN_10EC&DEV_8125", @"PCI\VEN_10EC&DEV_8168"),
        F(88, "Realtek 8125 LAN", "realtek", "lan", @"PCI\VEN_10EC&DEV_8125", @"PCI\VEN_10EC&DEV_8125&SUBSYS_E0001458"),
        F(89, "Realtek 8126 LAN", "realtek", "lan", @"PCI\VEN_10EC&DEV_8125", @"PCI\VEN_10EC&DEV_8126"),
        F(5, "AMD RAID (preinstall)", "amd", "storage", @"PCI\CC_010802", @"PCI\VEN_1022&DEV_43BD&CC_0104"),
        F(6, "AMD RAID", "amd", "storage", @"PCI\CC_010802", @"PCI\VEN_1022&DEV_43BD&CC_0104"),
        F(3, "AMD Graphics", "amd", "graphics", @"PCI\VEN_1002&DEV_164E", @"PCI\VEN_1022&DEV_15E3", @"PCI\AMDPCIE.DEVICEDESC"),
        F(1, "Realtek Audio", "realtek", "audio", @"SWC\VEN_10EC&HID_0001", @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897"),
        F(71, "NVIDIA Graphics", "nvidia", "graphics", @"PCI\VEN_10DE&DEV_2782"),
        F(2, "AMD Chipset", "amd", "chipset", @"ACPI\AMDI0030", @"ACPI\AMDI0010"),
    ];

    public static readonly WaterLevel[] Water =
    [
        W(7, "Realtek Wi-Fi", "6001.16.175.0", "2026-03-02", "asus"),
        W(87, "Realtek 8168 LAN", "10.68.815.2023", "2023-09-01", "gigabyte"),
        W(88, "Realtek 8125 LAN", "1125.29.50.202", "2026-02-01", "wucatalog", upstream: true),
        W(89, "Realtek 8126 LAN", "1126.27.50.919", "2025-12-31", "asus"),
        W(6, "AMD RAID", "9.3.3.329", "2026-03-15", "wucatalog", upstream: true),
        W(3, "AMD Graphics", "32.0.31041.1004", "2026-09-03", "wucatalog", upstream: true),
        W(1, "Realtek Audio", "6.4.0.2443", "2026-05-18", "msi"),
        W(71, "NVIDIA Graphics", "616.92", "2026-09-09", "asus"),
        W(2, "AMD Chipset", "7.12.30.211", "2026-02-05", "gigabyte"),
    ];

    public static readonly Artefact[] Artefacts =
    [
        A(7, "asus", "6001.16.175.0", "2026-03-02"),
        A(87, "gigabyte", "10.68.815.2023", "2023-09-01"),
        A(88, "msi", "10.79.50.1003", "2025-12-10", "https://download.msi.com/dvr_exe/mb/realtek_pcielan_w11.zip"),
        A(88, "asrock", "10.080.0525.2026", "2026-09-16", "https://download.asrock.com/Drivers/All/LAN/Realtek_LAN(v10.080.0525.2026).zip"),
        A(88, "msi", "10.080.0525.2026", "2026-09-20", "https://download.msi.com/dvr_exe/mb/realtek_lan_10080.zip"),
        A(88, "wucatalog", "1125.29.50.202", "2026-02-01", "https://www.catalog.update.microsoft.com/Search.aspx?q=PCI%5CVEN_10EC%26DEV_8125"),
        A(88, "gigabyte", "11.29.50.0202", "2026-06-23"),
        A(89, "asus", "1126.27.50.919", "2025-12-31"),
        A(6, "wucatalog", "9.3.3.329", "2026-03-15"),
        A(3, "lenovo", "31.0.24002.92", "2025-05-10"),
        A(3, "dell", "31.0.24028.1001", "2025-09-18"),
        A(3, "wucatalog", "32.0.31041.1004", "2026-09-03"),
        A(3, "msi", "25.10.36", "2025-12-26"),
        A(1, "gigabyte", "6.0.9927.1", "2026-01-15"),
        A(1, "asrock", "9360.1_UAD_WHQL_Nahimic", "2024-06-07"),
        A(1, "msi", "6.4.0.2443", "2026-05-18"),
        A(1, "lenovo", "1.4.2.55", "2024-02-01"),
        A(71, "lenovo", "32.0.15.9186", "2026-06-11"),
        A(71, "asus", "616.92", "2026-09-09"),
        A(71, "lenovo", "1.4.0.39", "2024-12-30"),
        A(2, "lenovo", "2.2.0.134", "2025-09-15"),
        A(2, "dell", "2.9.1.1935", "2023-03-01"),
        A(2, "gigabyte", "7.12.30.211", "2026-02-05"),
        A(2, "msi", "8.03.25.247", "2026-09-18", beta: true),
    ];

    public static LocalDevice Device(string name, string version, string provider, string inf, string[] hardwareIds,
        string[]? compatibleIds = null, string? driverDate = null) => new()
    {
        InstanceId = hardwareIds.FirstOrDefault() + @"\1",
        Name = name,
        HardwareIds = hardwareIds,
        CompatibleIds = compatibleIds ?? [],
        DriverVersion = version,
        DriverProvider = provider,
        InfPath = inf,
        DriverDate = driverDate is null ? null : DateTimeOffset.Parse(driverDate + "T00:00:00Z"),
    };

    /// <summary>Windows' real ID list for this PC's RTL8125 NIC.</summary>
    public static LocalDevice Rtl8125(string version = "10.79.50.1003") => Device(
        "Realtek Gaming 2.5GbE Family Controller", version, "Realtek", "oem114.inf",
        [
            @"PCI\VEN_10EC&DEV_8125&SUBSYS_7D731462&REV_05", @"PCI\VEN_10EC&DEV_8125&SUBSYS_7D731462",
            @"PCI\VEN_10EC&DEV_8125&CC_020000", @"PCI\VEN_10EC&DEV_8125&CC_0200",
        ],
        [
            @"PCI\VEN_10EC&DEV_8125&REV_05", @"PCI\VEN_10EC&DEV_8125", @"PCI\VEN_10EC&CC_020000",
            @"PCI\VEN_10EC&CC_0200", @"PCI\VEN_10EC", @"PCI\CC_020000", @"PCI\CC_0200",
        ],
        "2025-10-03");

    private static Family F(int id, string name, string vendor, string component, params string[] hwids) =>
        new() { FamilyId = id, Name = name, SiliconVendor = vendor, Component = component, Hwids = [.. hwids] };

    private static WaterLevel W(int id, string name, string version, string date, string by, bool upstream = false) =>
        new() { FamilyId = id, Family = name, Version = version, FirstPublished = date, PublishedBy = [by], UpstreamOnly = upstream };

    private static Artefact A(int familyId, string vendor, string version, string date, string? url = null, bool beta = false) =>
        new()
        {
            ArtefactId = Interlocked.Increment(ref _artefactId),
            Vendor = vendor,
            Kind = "driver",
            FamilyId = familyId,
            VersionRaw = version,
            ReleaseDate = date,
            Url = url ?? $"https://example.invalid/{vendor}/{version}.zip",
            IsBeta = beta ? 1 : 0,
        };
}
