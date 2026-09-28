namespace WdiCheck.Core.Inventory;

/// <summary>A present PnP device and the driver Windows has bound to it.</summary>
public sealed record LocalDevice
{
    public string InstanceId { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Class { get; init; }
    public string? Manufacturer { get; init; }

    /// <summary>Hardware IDs, most specific first.</summary>
    public IReadOnlyList<string> HardwareIds { get; init; } = [];

    /// <summary>Compatible IDs, most specific first.</summary>
    public IReadOnlyList<string> CompatibleIds { get; init; } = [];

    /// <summary>INF DriverVer version (what Device Manager shows).</summary>
    public string? DriverVersion { get; init; }

    public DateTimeOffset? DriverDate { get; init; }
    public string? DriverProvider { get; init; }
    public string? DriverDescription { get; init; }

    /// <summary>Installed INF name, e.g. <c>oem114.inf</c> (third-party) or <c>stornvme.inf</c> (inbox).</summary>
    public string? InfPath { get; init; }

    /// <summary>
    /// The INF's name as its vendor shipped it (e.g. <c>rt640x64.inf</c> for <c>oem114.inf</c>) — the key
    /// infs.json uses. Equal to <see cref="InfPath"/> for in-box drivers; null if it couldn't be resolved.
    /// </summary>
    public string? OriginalInfName { get; init; }

    /// <summary>True when the driver is one Windows ships in-box rather than a third-party package.</summary>
    public bool IsInboxDriver =>
        (DriverProvider?.Trim().Equals("Microsoft", StringComparison.OrdinalIgnoreCase) ?? false)
        || (!string.IsNullOrEmpty(InfPath) && !InfPath.StartsWith("oem", StringComparison.OrdinalIgnoreCase));

    public bool HasDriver => !string.IsNullOrEmpty(DriverVersion);
}

public interface IDeviceInventory
{
    Task<IReadOnlyList<LocalDevice>> GetDevicesAsync(CancellationToken ct);
}
