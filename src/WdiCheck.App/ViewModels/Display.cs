using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Windows.UI.Text;
using WdiCheck.Core.Matching;

namespace WdiCheck.App.ViewModels;

public enum Severity
{
    Good,
    Warning,
    Critical,
    Neutral,
}

/// <summary>Formatting shared by pages; also used from x:Bind function bindings.</summary>
public static class Display
{
    private static readonly Dictionary<string, string> ComponentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chipset"] = "Chipset",
        ["graphics"] = "Graphics",
        ["audio"] = "Audio",
        ["lan"] = "Ethernet",
        ["wlan"] = "Wi-Fi",
        ["bluetooth"] = "Bluetooth",
        ["storage"] = "Storage",
        ["usb"] = "USB & peripherals",
        ["npu"] = "NPU",
        ["camera"] = "Camera",
        ["wwan"] = "Mobile broadband",
        ["utility"] = "Utilities",
        ["other"] = "Other",
    };

    public static string Component(string? component) =>
        component is not null && ComponentNames.TryGetValue(component, out var n) ? n : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(component ?? "Other");

    /// <summary>Same thresholds as the WinDriverIndex site: current, ≤ 1 year amber, &gt; 1 year red.</summary>
    public static Severity LagSeverity(int? lag) => lag switch
    {
        null => Severity.Warning,
        0 => Severity.Good,
        <= 365 => Severity.Warning,
        _ => Severity.Critical,
    };

    public static string Lag(int? days) => days switch
    {
        null => "behind",
        0 => "current",
        <= 365 => $"{days} d behind",
        _ => $"{(days.Value / 365.0).ToString("0.0", CultureInfo.CurrentCulture)} yr behind",
    };

    public static string Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    public static string Date(string? isoDate) => isoDate ?? "";

    public static string DateTime(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    public static string Vendor(string? vendor) => vendor?.ToLowerInvariant() switch
    {
        "msi" => "MSI",
        "asus" => "ASUS",
        "gigabyte" => "Gigabyte",
        "asrock" => "ASRock",
        "lenovo" => "Lenovo",
        "dell" => "Dell",
        "hp" => "HP",
        "wucatalog" => "Windows Update Catalog",
        "silicon" => "Silicon vendor",
        null => "",
        _ => vendor,
    };

    /// <summary>A family's silicon_vendor as a name; null for "oem" (the hardware vendor's own part) or unknown.</summary>
    public static string? SiliconVendor(string? vendor) => vendor?.ToLowerInvariant() switch
    {
        "amd" => "AMD",
        "nvidia" => "NVIDIA",
        "intel" => "Intel",
        "realtek" => "Realtek",
        "mediatek" => "MediaTek",
        "qualcomm" => "Qualcomm",
        "aquantia" => "Marvell (Aquantia)",
        "asmedia" => "ASMedia",
        "ite" => "ITE",
        "dts" => "DTS",
        "aspeed" => "ASPEED",
        "ami" => "AMI",
        _ => null,
    };

    public static (string Text, Severity Severity) Status(DeviceResult r) => r.Status switch
    {
        DriverStatus.Current when r.IsNewerThanIndex => ("Newer than index", Severity.Good),
        DriverStatus.Current => ("Current", Severity.Good),
        DriverStatus.Behind => ((r.IsApproximate ? "≈ " : "") + Lag(r.LagDays), LagSeverity(r.LagDays)),
        DriverStatus.NotComparable => ("Not comparable", Severity.Neutral),
        DriverStatus.Inbox => ("Windows driver", Severity.Neutral),
        _ => ("Not in index", Severity.Neutral),
    };

    // x:Bind helpers
    public static FontWeight Weight(bool bold) => bold ? FontWeights.SemiBold : FontWeights.Normal;

    public static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ShowText(string? text) => string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility Hide(bool hidden) => hidden ? Visibility.Collapsed : Visibility.Visible;
}
