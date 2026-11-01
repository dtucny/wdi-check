using System.Diagnostics;
using System.Text.RegularExpressions;
using WdiCheck.Core.Index;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Versions;

namespace WdiCheck.Core.WindowsUpdate;

/// <summary>A driver Windows Update offers this machine (not installed yet).</summary>
public sealed record WindowsUpdateOffer
{
    /// <summary>E.g. "Realtek Semiconductor Corp. - Net - 10.80.407.2026".</summary>
    public string Title { get; init; } = "";

    /// <summary>Parsed from the title's last " - " segment; Windows Update exposes no version property.</summary>
    public DriverVersion? Version { get; init; }

    public DateOnly? DriverDate { get; init; }
    public string HardwareId { get; init; } = "";
    public string? DriverClass { get; init; }
    public string? Provider { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }

    /// <summary>True for optional updates (Settings › Windows Update › Advanced options › Optional updates).</summary>
    public bool BrowseOnly { get; init; }

    public bool IsHidden { get; init; }
    public string? UpdateId { get; init; }

    /// <summary>True when one of the device's hardware or compatible IDs is the offer's hardware ID.</summary>
    public bool AppliesTo(LocalDevice device)
    {
        if (string.IsNullOrWhiteSpace(HardwareId))
            return false;
        var id = Hwid.Normalise(HardwareId);
        return device.HardwareIds.Concat(device.CompatibleIds).Any(d => Hwid.Normalise(d) == id);
    }

    /// <summary>
    /// True for a system-firmware package dated the same day as the installed BIOS: almost certainly the BIOS
    /// already installed, repackaged as a UEFI capsule. Windows offers it anyway when the firmware device only has
    /// Microsoft's generic driver, because the OEM package matches the device more specifically.
    /// </summary>
    public bool MatchesInstalledBios(SystemIdentity identity)
    {
        if (DriverDate is not { } date || identity.BiosReleaseDate != date)
            return false;
        if (!string.Equals(DriverClass, "Firmware", StringComparison.OrdinalIgnoreCase) || FirmwareResource is not { } resource)
            return false;
        // Without an ESRT to say which resource is the BIOS, class and date decide.
        return identity.SystemFirmwareResources.Count == 0 || identity.SystemFirmwareResources.Contains(resource);
    }

    /// <summary>The resource GUID of a <c>UEFI\RES_{guid}</c> hardware ID.</summary>
    public Guid? FirmwareResource =>
        HardwareId.StartsWith(FirmwarePrefix, StringComparison.OrdinalIgnoreCase)
        && Guid.TryParse(HardwareId.AsSpan(FirmwarePrefix.Length), out var g)
            ? g
            : null;

    private const string FirmwarePrefix = @"UEFI\RES_";
}

public sealed record WindowsUpdateResult(IReadOnlyList<WindowsUpdateOffer> Offers, string? Error, DateTimeOffset SearchedAt, TimeSpan Duration)
{
    public bool Succeeded => Error is null;
}

public interface IWindowsUpdateDriverSource
{
    Task<WindowsUpdateResult> SearchAsync(CancellationToken ct);
}

/// <summary>
/// Asks the Windows Update Agent which drivers it offers this machine: an <c>UpdateSession</c> searcher,
/// searched with <c>IsInstalled=0 and Type='Driver'</c> against Windows Update. Unlike a Catalog search, this
/// applies Microsoft's machine-specific targeting — the authoritative answer for anything Windows Update supplies.
/// Searching needs no elevation and installs nothing.
/// <para>
/// The search runs on its own thread (see <see cref="WindowsUpdateAgent"/>). A slow or stuck search (it can take a
/// minute) is abandoned on cancellation or timeout: the result returns at once, and the thread aborts the search
/// and winds down in the background.
/// </para>
/// </summary>
public sealed partial class WindowsUpdateDrivers : IWindowsUpdateDriverSource
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    public async Task<WindowsUpdateResult> SearchAsync(CancellationToken ct)
    {
        var started = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var updates = await SearchOnOwnThreadAsync(timeout.Token).ConfigureAwait(false);
            return new WindowsUpdateResult(updates.Select(ToOffer).ToList(), null, started, sw.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new WindowsUpdateResult([], $"Windows Update didn't answer within {Timeout.TotalMinutes:0} minutes.", started, sw.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WindowsUpdateResult([], Describe(ex), started, sw.Elapsed);
        }
    }

    private static Task<List<AgentDriverUpdate>> SearchOnOwnThreadAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<List<AgentDriverUpdate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Completes at once on cancellation; the thread finishes aborting on its own.
        var registration = ct.Register(() => tcs.TrySetCanceled(ct));
        var thread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(WindowsUpdateAgent.SearchDrivers(ct));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                registration.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "Windows Update search",
        };
        thread.Start();
        return tcs.Task;
    }

    internal static WindowsUpdateOffer ToOffer(AgentDriverUpdate u) => new()
    {
        Title = u.Title ?? "",
        Version = VersionFromTitle(u.Title),
        DriverDate = u.DriverVerDate is { } d ? DateOnly.FromDateTime(d) : null,
        HardwareId = u.HardwareId ?? "",
        DriverClass = u.DriverClass,
        Provider = u.Provider,
        Manufacturer = u.Manufacturer,
        Model = u.Model,
        BrowseOnly = u.BrowseOnly,
        IsHidden = u.IsHidden,
        UpdateId = u.UpdateId,
    };

    /// <summary>A failed search as one line, keeping the HRESULT for anyone looking it up.</summary>
    internal static string Describe(Exception ex)
    {
        var code = $"0x{ex.HResult:X8}";
        var text = unchecked((uint)ex.HResult) switch
        {
            0x80070422 => "The Windows Update service is disabled.",
            // WU_E_WU_DISABLED, WU_E_REDIRECTOR_CONNECT_POLICY
            0x8024002E or 0x8024500C => "Policy on this PC blocks searching Windows Update directly.",
            // WU_E_PT_WINHTTP_NAME_NOT_RESOLVED, and WinHTTP's own timeout and connection errors
            0x8024402C or 0x80072EE2 or 0x80072EE7 or 0x80072EFD => "Couldn't reach Windows Update. Check the network connection.",
            _ => ex.Message,
        };
        return text.Contains(code, StringComparison.OrdinalIgnoreCase) ? text : $"{text} ({code})";
    }

    /// <summary>"Realtek Semiconductor Corp. - Net - 10.80.407.2026" → 10.80.407.2026 (the last " - " segment).</summary>
    internal static DriverVersion? VersionFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;
        // The version as written (keeping "10.080"-style padding): the longest dotted run in the last " - "
        // segment, or anywhere in titles that don't follow the "Provider - Class - Version" pattern.
        return LongestRun(title.Split(" - ")[^1]) ?? LongestRun(title);

        static DriverVersion? LongestRun(string text)
        {
            var best = DottedRegex().Matches(text).Select(m => m.Value).MaxBy(v => v.Length);
            return best is null ? null : DriverVersion.Parse(best);
        }
    }

    [GeneratedRegex(@"\d+(?:\.\d+){2,}")]
    private static partial Regex DottedRegex();
}
