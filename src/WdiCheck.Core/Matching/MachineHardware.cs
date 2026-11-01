using System.Text.RegularExpressions;
using WdiCheck.Core.Index;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Reporting;

namespace WdiCheck.Core.Matching;

/// <summary>
/// The chip vendor's own newest release for a device: the newest INF from their installer that binds it, or, where
/// the index hasn't unpacked that installer, the family's newest release from the index's "silicon" source.
/// </summary>
/// <param name="SameNumbering">
/// False when it's on a different version line from the installed driver: usually a package version (AMD's chipset
/// package 8.08.12.551 holds drivers numbered 3.0.5.0), so the two can't be compared by eye.
/// </param>
public sealed record SiliconRelease(string Version, DateOnly? Date, Uri? Url, bool SameNumbering);

/// <summary>One line of the machine summary: a piece of hardware and the driver that represents it.</summary>
public sealed record HardwareItem
{
    /// <summary>"processor", "graphics", or the family's component for the rest.</summary>
    public required string Component { get; init; }

    public required string Name { get; init; }

    /// <summary>For graphics: "built-in" or "MSI card"; null when it can't be told.</summary>
    public string? Detail { get; init; }

    /// <summary>Processor and graphics; the rest only appear in the full summary.</summary>
    public bool IsCore { get; init; }

    /// <summary>
    /// The GPU itself for graphics; for the rest, the device furthest behind among <see cref="Devices"/>. Null when
    /// none has a driver the index knows.
    /// </summary>
    public DeviceResult? Driver { get; init; }

    public IReadOnlyList<DeviceResult> Devices { get; init; } = [];

    public SiliconRelease? Silicon { get; init; }
}

/// <summary>
/// What's in this machine, as driver families relate to hardware: the processor (with its chipset driver), each GPU,
/// then every other family the index compares. Built from a scan's results; nothing is read here but the device list.
/// </summary>
public static partial class MachineHardware
{
    private const string Silicon = "silicon";

    /// <summary>Full-summary order: network first, then the rest roughly by how often people look for them.</summary>
    private static readonly string[] ComponentOrder = ["lan", "wlan", "bluetooth", "audio", "storage", "npu", "usb", "camera", "wwan"];

    public static IReadOnlyList<HardwareItem> Build(SystemIdentity identity, IReadOnlyList<DeviceResult> results, InfIndex? infs = null)
    {
        var comparable = results.Where(IsComparable).ToList();
        var items = new List<HardwareItem>();

        var chipset = comparable.Where(r => r.Component == "chipset").ToList();
        if (identity.ProcessorName is not null || chipset.Count > 0)
            items.Add(Item("processor", identity.ProcessorName ?? "Processor", null, true, chipset, infs));

        // Each GPU, with its vendor's other devices (control panels, OpenCL, audio over HDMI) in the same family.
        var boardSsid = BoardSubsystem(results);
        var gpus = results.Where(r => IsGpu(r.Device)).ToList();
        var claimed = new HashSet<DeviceResult>(chipset);
        foreach (var gpu in gpus)
        {
            var devices = new List<DeviceResult> { gpu };
            if (gpu.Family is { } family)
                devices.AddRange(comparable.Where(r => r.Family == family && !gpus.Contains(r) && !claimed.Contains(r)));
            claimed.UnionWith(devices);
            items.Add(Item("graphics", gpu.Device.Name, GraphicsDetail(gpu.Device, boardSsid), true, devices, infs, gpu));
        }

        items.AddRange(comparable
            .Where(r => !claimed.Contains(r))
            .GroupBy(r => r.Family!)
            .OrderBy(g => Order(g.Key.Component))
            .ThenBy(g => g.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => Item(g.Key.Component, Worst(g)!.Device.Name, null, false, g.ToList(), infs)));
        return items;
    }

    private static HardwareItem Item(string component, string name, string? detail, bool isCore, IReadOnlyList<DeviceResult> devices, InfIndex? infs,
        DeviceResult? primary = null)
    {
        // A GPU row describes the GPU's driver, not its vendor's noise-suppression component that happens to lag more.
        var driver = primary is not null && IsComparable(primary) ? primary : Worst(devices.Where(IsComparable)) ?? devices.FirstOrDefault();
        return new HardwareItem
        {
            Component = component,
            Name = name,
            Detail = detail,
            IsCore = isCore,
            Driver = driver,
            Devices = devices,
            Silicon = driver is null ? null : NewestFromSilicon(driver, infs),
        };
    }

    private static bool IsComparable(DeviceResult r) =>
        r.Family is not null && r.Status is not (DriverStatus.Inbox or DriverStatus.NotIndexed);

    /// <summary>Behind (longest lag first) before anything else, then current.</summary>
    private static DeviceResult? Worst(IEnumerable<DeviceResult> results) => results
        .OrderBy(r => r.Status switch { DriverStatus.Behind => 0, DriverStatus.Current => 1, _ => 2 })
        .ThenByDescending(r => r.LagDays ?? 0)
        .FirstOrDefault();

    private static int Order(string component) =>
        Array.IndexOf(ComponentOrder, component) is var i and >= 0 ? i : ComponentOrder.Length;

    internal static bool IsGpu(LocalDevice d) =>
        string.Equals(d.Class, "Display", StringComparison.OrdinalIgnoreCase)
        && d.HardwareIds.Any(id => id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The chip vendor's newest release for this device. One release can be several driver branches in several
    /// installers (AMD 26.10.2: 32.0.32015.x for RDNA3/4, 32.0.21045.x for RDNA2 and Raphael), so where the index
    /// has unpacked the family's installers, only an INF from them that binds the device counts, never the family's
    /// newest.
    /// </summary>
    internal static SiliconRelease? NewestFromSilicon(DeviceResult driver, InfIndex? infs)
    {
        if (driver.Family is not { } family)
            return null;
        var listed = NewestListedBySilicon(family, driver.Device.DriverVersion);
        if (driver.Inf?.Releases.FirstOrDefault(r => r.IsFromSilicon) is { } inf)
        {
            // Releases are newest first. Shown in the family's numbering (NVIDIA 32.0.16.2101 → 621.01), like the page.
            return new SiliconRelease(family.Effective(inf.Row.DriverVer).Raw, inf.FirstPublished, listed?.Url, true);
        }
        // Their INFs were read and none binds this device in its numbering: the family's release isn't for it.
        // A release numbered differently (a chipset package) was never comparable by INF; keep it.
        if (infs?.HasSiliconRows(family.Id) == true && listed is { SameNumbering: true })
            return null;
        return listed;
    }

    /// <summary>The newest non-beta version a chip vendor published itself, with its download page.</summary>
    public static SiliconRelease? NewestListedBySilicon(FamilyInfo family, string? installed)
    {
        foreach (var known in family.Versions)
        {
            var artefacts = known.Artefacts.Where(a => a.IsBeta == 0 && a.Vendor.Equals(Silicon, StringComparison.OrdinalIgnoreCase)).ToList();
            if (artefacts.Count == 0)
                continue;
            var url = artefacts.Select(a => a.Url).FirstOrDefault(DownloadLinks.IsSafe);
            return new SiliconRelease(
                known.Version.Raw,
                artefacts.Select(a => FamilyInfo.ParseDate(a.ReleaseDate)).Where(d => d is not null).Min(),
                url is null ? null : new Uri(url),
                family.Effective(installed).Line is { } line && known.Version.Line == line);
        }
        return null;
    }

    /// <summary>
    /// "built-in" when the GPU carries the board's PCI subsystem ID (an iGPU, or a laptop's discrete GPU), else the
    /// card maker from the subsystem vendor. Null when the board's own subsystem ID can't be told, rather than
    /// calling an iGPU a card.
    /// </summary>
    internal static string? GraphicsDetail(LocalDevice gpu, string? boardSsid)
    {
        if (Pci(gpu) is not { Ssid: var ssid } || boardSsid is null)
            return null;
        if (ssid == boardSsid)
            return "built-in";
        return PciVendorName(ssid[4..]) is { } maker ? $"{maker} card" : "add-in card";
    }

    /// <summary>
    /// The subsystem ID most onboard devices share: the board maker stamps its own on the chipset, audio, LAN and
    /// iGPU. Skips none (all zeros) and chip makers' own (subsystem vendor = device vendor, e.g. ASMedia switches).
    /// </summary>
    internal static string? BoardSubsystem(IEnumerable<DeviceResult> results) => results
        .Select(r => Pci(r.Device))
        .Where(p => p is { } x && x.Ssid != "00000000" && !x.Ssid.EndsWith(x.Vendor, StringComparison.Ordinal))
        .GroupBy(p => p!.Value.Ssid)
        .Where(g => g.Count() >= 2)
        .OrderByDescending(g => g.Count())
        .Select(g => g.Key)
        .FirstOrDefault();

    private static (string Vendor, string Ssid)? Pci(LocalDevice d)
    {
        foreach (var id in d.HardwareIds)
        {
            var m = PciIdRegex().Match(id);
            if (m.Success)
                return (m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value.ToUpperInvariant());
        }
        return null;
    }

    [GeneratedRegex(@"^PCI\\VEN_([0-9A-F]{4})&DEV_[0-9A-F]{4}&SUBSYS_([0-9A-F]{8})", RegexOptions.IgnoreCase)]
    private static partial Regex PciIdRegex();

    /// <summary>PCI subsystem vendors that build graphics cards (or machines with them).</summary>
    internal static string? PciVendorName(string vendorId) => vendorId.ToUpperInvariant() switch
    {
        "1002" => "AMD",
        "10DE" => "NVIDIA",
        "8086" => "Intel",
        "1462" => "MSI",
        "1043" => "ASUS",
        "1458" => "Gigabyte",
        "1849" => "ASRock",
        "19DA" => "Zotac",
        "3842" => "EVGA",
        "1569" => "Palit",
        "10B0" => "Gainward",
        "1B4C" => "Galax",
        "7377" => "Colorful",
        "196E" => "PNY",
        "1682" => "XFX",
        "1DA2" => "Sapphire",
        "148C" => "PowerColor",
        "1028" => "Dell",
        "103C" => "HP",
        "17AA" => "Lenovo",
        "1025" => "Acer",
        _ => null,
    };
}
