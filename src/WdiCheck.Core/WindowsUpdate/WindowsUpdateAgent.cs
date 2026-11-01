using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.UpdateAgent;

namespace WdiCheck.Core.WindowsUpdate;

/// <summary>A driver update as the Windows Update Agent describes it, before <see cref="WindowsUpdateDrivers.ToOffer"/>.</summary>
internal sealed record AgentDriverUpdate(
    string? Title,
    DateTime? DriverVerDate,
    string? HardwareId,
    string? DriverClass,
    string? Provider,
    string? Manufacturer,
    string? Model,
    bool BrowseOnly,
    bool IsHidden,
    string? UpdateId);

/// <summary>
/// The Windows Update Agent's COM API (<c>wuapi.dll</c>), through CsWin32's marshalling-free bindings
/// (NativeMethods.txt): the trimmed app has built-in COM interop disabled, and these need none.
/// </summary>
internal static unsafe class WindowsUpdateAgent
{
    internal const string Criteria = "IsInstalled=0 and Type='Driver'";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Searches synchronously on the calling thread, which it joins to the MTA, so call it on a dedicated thread.
    /// The search job is polled (see <see cref="NoOpSearchCallback"/>). On cancellation, asks the agent to abort and
    /// throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static List<AgentDriverUpdate> SearchDrivers(CancellationToken ct)
    {
        PInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED).ThrowOnFailure();
        IUpdateSession* session = null;
        IUpdateSearcher* searcher = null;
        ISearchJob* job = null;
        ISearchResult* result = null;
        IUpdateCollection* updates = null;
        try
        {
            UpdateSession.CreateInstance(out session).ThrowOnFailure();
            using (var appId = new OwnedBstr("WdiCheck"))
                session->ClientApplicationID = appId.Value;
            searcher = session->CreateUpdateSearcher();
            // Ask Windows Update itself, even on a WSUS-managed machine.
            searcher->ServerSelection = ServerSelection.ssWindowsUpdate;

            using (var criteria = new OwnedBstr(Criteria))
                job = searcher->BeginSearch(criteria.Value, (IUnknown*)NoOpSearchCallback.Instance, default);

            // IsCancellationRequested, not ct.WaitHandle: the caller may already have disposed the token's source.
            while (!job->IsCompleted)
            {
                if (ct.IsCancellationRequested)
                {
                    job->RequestAbort();
                    ct.ThrowIfCancellationRequested();
                }
                Thread.Sleep(PollInterval);
            }

            result = searcher->EndSearch(job);
            if (result->ResultCode is OperationResultCode.orcFailed or OperationResultCode.orcAborted)
                throw new COMException(result->ResultCode is OperationResultCode.orcAborted
                    ? "The Windows Update search was cancelled."
                    : "The Windows Update search failed.");

            updates = result->Updates;
            var list = new List<AgentDriverUpdate>();
            for (var i = 0; i < updates->Count; i++)
            {
                var update = updates->get_Item(i);
                try
                {
                    if (update->QueryInterface(out IWindowsDriverUpdate* driver).Failed)
                        continue;
                    try
                    {
                        list.Add(Read(update, driver));
                    }
                    finally
                    {
                        driver->Release();
                    }
                }
                finally
                {
                    update->Release();
                }
            }
            return list;
        }
        finally
        {
            if (updates is not null) updates->Release();
            if (result is not null) result->Release();
            if (job is not null)
            {
                // Waits for an aborted search to wind down, and is required after EndSearch too.
                try { job->CleanUp(); } catch (COMException) { }
                job->Release();
            }
            if (searcher is not null) searcher->Release();
            if (session is not null) session->Release();
            PInvoke.CoUninitialize();
        }
    }

    private static AgentDriverUpdate Read(IUpdate* update, IWindowsDriverUpdate* driver)
    {
        var identity = update->Identity;
        string? updateId;
        try
        {
            updateId = Take(identity->UpdateID);
        }
        finally
        {
            identity->Release();
        }

        // BrowseOnly (optional updates) arrived with IUpdate3, Windows 7.
        var browseOnly = false;
        if (update->QueryInterface(out IUpdate3* update3).Succeeded)
        {
            browseOnly = update3->BrowseOnly;
            update3->Release();
        }

        // An OLE DATE; 0 (1899-12-30) means none.
        var date = driver->DriverVerDate;
        return new AgentDriverUpdate(
            Take(update->Title),
            date > 0 ? DateTime.FromOADate(date) : null,
            Take(driver->DriverHardwareID),
            Take(driver->DriverClass),
            Take(driver->DriverProvider),
            Take(driver->DriverManufacturer),
            Take(driver->DriverModel),
            browseOnly,
            update->IsHidden,
            updateId);
    }

    /// <summary>Copies and frees a BSTR the agent handed over.</summary>
    private static string? Take(BSTR bstr)
    {
        if (bstr.Value is null)
            return null;
        try
        {
            return Marshal.PtrToStringBSTR((nint)bstr.Value);
        }
        finally
        {
            Marshal.FreeBSTR((nint)bstr.Value);
        }
    }

    /// <summary>
    /// BeginSearch fails with E_POINTER without a completion callback, but the search is polled, so this one does
    /// nothing. It's a static COM object, allocated once and never freed, so its reference count is moot.
    /// </summary>
    private static class NoOpSearchCallback
    {
        public static readonly ISearchCompletedCallback* Instance = Create();

        private static ISearchCompletedCallback* Create()
        {
            var vtbl = (ISearchCompletedCallback.Vtbl*)NativeMemory.Alloc((nuint)sizeof(ISearchCompletedCallback.Vtbl));
            vtbl->QueryInterface_1 = &QueryInterface;
            vtbl->AddRef_2 = &AddRef;
            vtbl->Release_3 = &Release;
            vtbl->Invoke_4 = &Invoke;
            var obj = (void**)NativeMemory.Alloc((nuint)sizeof(void*));
            *obj = vtbl;
            return (ISearchCompletedCallback*)obj;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static HRESULT QueryInterface(ISearchCompletedCallback* self, Guid* riid, void** ppv)
        {
            if (*riid == IUnknown.IID_Guid || *riid == ISearchCompletedCallback.IID_Guid)
            {
                *ppv = self;
                return HRESULT.S_OK;
            }
            *ppv = null;
            return HRESULT.E_NOINTERFACE;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint AddRef(ISearchCompletedCallback* self) => 1;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint Release(ISearchCompletedCallback* self) => 1;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static HRESULT Invoke(ISearchCompletedCallback* self, ISearchJob* job, ISearchCompletedCallbackArgs* args) => HRESULT.S_OK;
    }

    private readonly struct OwnedBstr(string value) : IDisposable
    {
        public readonly BSTR Value = (BSTR)(char*)Marshal.StringToBSTR(value);

        public void Dispose() => Marshal.FreeBSTR((nint)Value.Value);
    }
}
