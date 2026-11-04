using System.Runtime.InteropServices;

namespace WdiCheck.App;

/// <summary>
/// Raises <see cref="Reset"/> when the graphics driver resets: it was updated, or recovered from a hang (TDR).
/// WinUI redraws the window's shapes after that but not always its text (seen after an AMD driver update on a
/// Legion Slim 5), and only a new process draws normally again. Watches with a Direct3D device of its own on the
/// default adapter, which a reset removes along with the window's.
/// </summary>
internal sealed unsafe class GpuResetWatcher : IDisposable
{
    private static readonly Guid IidD3D11Device4 = new("8992ab71-02e6-4b8d-ba48-b056dcda42c4");

    // ID3D11Device4's vtable (d3d11_4.h): IUnknown, then ID3D11Device to ID3D11Device3, then these two.
    private const int RegisterDeviceRemovedEventSlot = 65;
    private const int UnregisterDeviceRemovedSlot = 66;

    private readonly Lock _lock = new();
    private nint _device;
    private uint _cookie;
    private EventWaitHandle? _removed;
    private RegisteredWaitHandle? _wait;
    private bool _disposed;

    /// <summary>On a thread-pool thread.</summary>
    public event Action? Reset;

    /// <summary>Start watching; false when Direct3D 11.4 isn't available (no watching, nothing else changes).</summary>
    public bool Start()
    {
        lock (_lock)
        {
            if (_disposed || _device != 0)
                return _device != 0;
            nint created = 0;
            try
            {
                if (D3D11CreateDevice(0, DriverTypeHardware, 0, 0, null, 0, SdkVersion, &created, null, null) < 0 || created == 0)
                    return false;
                var iid = IidD3D11Device4;
                if (Marshal.QueryInterface(created, in iid, out var device4) < 0)
                    return false;
                _device = device4;

                _removed = new EventWaitHandle(false, EventResetMode.ManualReset);
                uint cookie;
                var register = (delegate* unmanaged[Stdcall]<nint, nint, uint*, int>)VtableSlot(_device, RegisterDeviceRemovedEventSlot);
                if (register(_device, _removed.SafeWaitHandle.DangerousGetHandle(), &cookie) < 0)
                {
                    StopLocked();
                    return false;
                }
                _cookie = cookie;
                _wait = ThreadPool.RegisterWaitForSingleObject(_removed, (_, _) => OnRemoved(), null, Timeout.Infinite, executeOnlyOnce: true);
                return true;
            }
            finally
            {
                if (created != 0)
                    Marshal.Release(created);
            }
        }
    }

    private void OnRemoved()
    {
        lock (_lock)
            StopLocked();
        Reset?.Invoke();
    }

    private void StopLocked()
    {
        _wait?.Unregister(null);
        _wait = null;
        if (_device != 0)
        {
            if (_cookie != 0)
                ((delegate* unmanaged[Stdcall]<nint, uint, void>)VtableSlot(_device, UnregisterDeviceRemovedSlot))(_device, _cookie);
            Marshal.Release(_device);
        }
        _device = 0;
        _cookie = 0;
        _removed?.Dispose();
        _removed = null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            StopLocked();
        }
    }

    private static nint VtableSlot(nint com, int slot) => (*(nint**)com)[slot];

    private const int DriverTypeHardware = 1;
    private const uint SdkVersion = 7;

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, int* featureLevels,
        uint featureLevelCount, uint sdkVersion, nint* device, int* featureLevel, nint* immediateContext);
}
