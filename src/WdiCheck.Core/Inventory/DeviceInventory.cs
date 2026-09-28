using System.Runtime.InteropServices;
using System.Text;
using Windows.Devices.Enumeration;

namespace WdiCheck.Core.Inventory;

/// <summary>
/// Enumerates present device nodes through Windows.Devices.Enumeration and reads the
/// driver properties (DEVPKEY_Device_Driver*) Device Manager shows.
/// </summary>
public sealed class DeviceInventory : IDeviceInventory
{
    // DEVPKEY_Device_DriverDate / DriverVersion / DriverDesc / DriverInfPath / DriverProvider
    private const string DriverDateKey = "{a8b865dd-2e3d-4094-ad97-e593a70c75d6} 2";
    private const string DriverVersionKey = "{a8b865dd-2e3d-4094-ad97-e593a70c75d6} 3";
    private const string DriverDescKey = "{a8b865dd-2e3d-4094-ad97-e593a70c75d6} 4";
    private const string DriverInfPathKey = "{a8b865dd-2e3d-4094-ad97-e593a70c75d6} 5";
    private const string DriverProviderKey = "{a8b865dd-2e3d-4094-ad97-e593a70c75d6} 9";
    // DEVPKEY_Device_Class / DEVPKEY_Device_Manufacturer
    private const string ClassKey = "{a45c254e-df1c-4efd-8020-67d146a850e0} 9";
    private const string ManufacturerKey = "{a45c254e-df1c-4efd-8020-67d146a850e0} 13";
    private const string HardwareIdsKey = "System.Devices.HardwareIds";
    private const string CompatibleIdsKey = "System.Devices.CompatibleIds";

    private static readonly string[] Properties =
    [
        HardwareIdsKey, CompatibleIdsKey, DriverDateKey, DriverVersionKey, DriverDescKey,
        DriverInfPathKey, DriverProviderKey, ClassKey, ManufacturerKey,
    ];

    private const string PresentFilter = "System.Devices.Present:=System.StructuredQueryType.Boolean#True";

    public async Task<IReadOnlyList<LocalDevice>> GetDevicesAsync(CancellationToken ct)
    {
        var infos = await DeviceInformation
            .FindAllAsync(PresentFilter, Properties, DeviceInformationKind.Device)
            .AsTask(ct)
            .ConfigureAwait(false);

        var originalNames = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var result = new List<LocalDevice>(infos.Count);
        foreach (var d in infos)
        {
            var p = d.Properties;
            var infPath = Str(p, DriverInfPathKey);
            string? original = null;
            if (infPath is not null && !originalNames.TryGetValue(infPath, out original))
                originalNames[infPath] = original = ResolveOriginalInfName(infPath);
            result.Add(new LocalDevice
            {
                InstanceId = d.Id,
                Name = string.IsNullOrWhiteSpace(d.Name) ? Str(p, DriverDescKey) ?? d.Id : d.Name,
                Class = Str(p, ClassKey),
                Manufacturer = Str(p, ManufacturerKey),
                HardwareIds = Strs(p, HardwareIdsKey),
                CompatibleIds = Strs(p, CompatibleIdsKey),
                DriverVersion = Str(p, DriverVersionKey),
                DriverDate = p.TryGetValue(DriverDateKey, out var date) && date is DateTimeOffset dto ? dto : null,
                DriverDescription = Str(p, DriverDescKey),
                DriverProvider = Str(p, DriverProviderKey),
                InfPath = infPath,
                OriginalInfName = original,
            });
        }

        return result;
    }

    /// <summary>
    /// oemNN.inf -> the vendor's INF name: SetupAPI reports the driver store copy
    /// (DriverStore\FileRepository\rt640x64.inf_amd64_...\rt640x64.inf), whose file name is the original. Works without elevation.
    /// </summary>
    public static string? ResolveOriginalInfName(string publishedInf)
    {
        try
        {
            var buffer = new StringBuilder(1024);
            return SetupGetInfDriverStoreLocationW(publishedInf, IntPtr.Zero, null, buffer, buffer.Capacity, out _)
                ? Path.GetFileName(buffer.ToString())
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupGetInfDriverStoreLocationW(string fileName, IntPtr alternatePlatformInfo, string? localeName,
        StringBuilder returnBuffer, int returnBufferSize, out int requiredSize);

    private static string? Str(IReadOnlyDictionary<string, object> p, string key) =>
        p.TryGetValue(key, out var v) && v is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    private static IReadOnlyList<string> Strs(IReadOnlyDictionary<string, object> p, string key) =>
        p.TryGetValue(key, out var v) && v is string[] a ? a.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() : [];
}
