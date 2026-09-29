using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Asks the Windows Update Agent which drivers it offers this machine: <c>Microsoft.Update.Session</c>,
/// searched with <c>IsInstalled=0 and Type='Driver'</c> against Windows Update. Unlike a Catalog search, this
/// applies Microsoft's machine-specific targeting — the authoritative answer for anything Windows Update supplies.
/// Searching needs no elevation and installs nothing.
/// <para>
/// Runs out of process in Windows PowerShell: the trimmed app has built-in COM interop disabled, and a separate
/// process lets a slow or stuck search (it can take a minute) be abandoned cleanly.
/// </para>
/// </summary>
public sealed partial class WindowsUpdateDrivers : IWindowsUpdateDriverSource
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    // ServerSelection 2 = ssWindowsUpdate: ask Windows Update itself even on a WSUS-managed machine.
    // Output is one JSON object: { "updates": [...] } or { "error": "..." }.
    internal const string Script = """
        $ErrorActionPreference = 'Stop'
        try {
            $session = New-Object -ComObject Microsoft.Update.Session
            $session.ClientApplicationID = 'WdiCheck'
            $searcher = $session.CreateUpdateSearcher()
            $searcher.ServerSelection = 2
            $result = $searcher.Search("IsInstalled=0 and Type='Driver'")
            $list = @(foreach ($u in $result.Updates) {
                [ordered]@{
                    title = [string]$u.Title
                    driverDate = if ($u.DriverVerDate) { ([datetime]$u.DriverVerDate).ToString('yyyy-MM-dd') } else { $null }
                    hardwareId = [string]$u.DriverHardwareID
                    driverClass = [string]$u.DriverClass
                    provider = [string]$u.DriverProvider
                    manufacturer = [string]$u.DriverManufacturer
                    model = [string]$u.DriverModel
                    browseOnly = [bool]$u.BrowseOnly
                    isHidden = [bool]$u.IsHidden
                    updateId = [string]$u.Identity.UpdateID
                }
            })
            ConvertTo-Json -InputObject ([ordered]@{ resultCode = [int]$result.ResultCode; updates = $list }) -Depth 4 -Compress
        } catch {
            ConvertTo-Json -InputObject ([ordered]@{ error = ('{0} (0x{1:X8})' -f $_.Exception.Message, $_.Exception.HResult) }) -Compress
        }
        """;

    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    public async Task<WindowsUpdateResult> SearchAsync(CancellationToken ct)
    {
        var started = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();
        try
        {
            var json = await RunScriptAsync(ct).ConfigureAwait(false);
            var (offers, error) = Parse(json);
            return new WindowsUpdateResult(offers, error, started, sw.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new WindowsUpdateResult([], $"Windows Update didn't answer within {Timeout.TotalMinutes:0} minutes.", started, sw.Elapsed);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new WindowsUpdateResult([], $"Couldn't start the Windows Update search: {ex.Message}", started, sw.Elapsed);
        }
    }

    private async Task<string> RunScriptAsync(CancellationToken ct)
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var psi = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Encode(Script) })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("powershell.exe did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = (await stdout.ConfigureAwait(false)).Trim();
            if (output.Length == 0)
                throw new InvalidOperationException((await stderr.ConfigureAwait(false)).Trim() is { Length: > 0 } e ? e : "no output");
            return output;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
    }

    // -EncodedCommand takes base64 of UTF-16LE, which sidesteps all quoting.
    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    internal static (IReadOnlyList<WindowsUpdateOffer> Offers, string? Error) Parse(string json)
    {
        ScriptOutput? output;
        try
        {
            output = JsonSerializer.Deserialize(json, WindowsUpdateJsonContext.Default.ScriptOutput);
        }
        catch (JsonException ex)
        {
            return ([], $"Unexpected output from the Windows Update search: {ex.Message}");
        }

        if (output is null)
            return ([], "No output from the Windows Update search.");
        if (!string.IsNullOrWhiteSpace(output.Error))
            return ([], output.Error);

        var offers = (output.Updates ?? []).Select(u => new WindowsUpdateOffer
        {
            Title = u.Title ?? "",
            Version = VersionFromTitle(u.Title),
            DriverDate = DateOnly.TryParseExact(u.DriverDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
            HardwareId = u.HardwareId ?? "",
            DriverClass = u.DriverClass,
            Provider = u.Provider,
            Manufacturer = u.Manufacturer,
            Model = u.Model,
            BrowseOnly = u.BrowseOnly,
            IsHidden = u.IsHidden,
            UpdateId = u.UpdateId,
        }).ToList();
        return (offers, null);
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

    internal sealed class ScriptOutput
    {
        public int? ResultCode { get; init; }
        public string? Error { get; init; }
        public List<ScriptUpdate>? Updates { get; init; }
    }

    internal sealed class ScriptUpdate
    {
        public string? Title { get; init; }
        public string? DriverDate { get; init; }
        public string? HardwareId { get; init; }
        public string? DriverClass { get; init; }
        public string? Provider { get; init; }
        public string? Manufacturer { get; init; }
        public string? Model { get; init; }
        public bool BrowseOnly { get; init; }
        public bool IsHidden { get; init; }
        public string? UpdateId { get; init; }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WindowsUpdateDrivers.ScriptOutput))]
internal partial class WindowsUpdateJsonContext : JsonSerializerContext
{
}
