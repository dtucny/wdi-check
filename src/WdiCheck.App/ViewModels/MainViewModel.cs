using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WdiCheck.Core.Api;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Reporting;
using WdiCheck.Core.Scanning;
using WdiCheck.Core.Settings;
using WdiCheck.Core.Updates;
using WdiCheck.Core.WindowsUpdate;
using Windows.Storage.Pickers;

namespace WdiCheck.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly HttpClient _http = WdiClient.CreateHttpClient();
    private readonly IWindowsUpdateDriverSource _windowsUpdateSource = new WindowsUpdateDrivers();
    private ScanService _scan;
    private CancellationTokenSource? _cts;
    private WindowsUpdateInfo _windowsUpdate = WindowsUpdateInfo.Off;

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        _scan = CreateScanService();
        ShowInboxAndUnmatched = settings.ShowInboxAndUnmatched;
    }

    public AppSettings Settings { get; }

    public WdiClient Client => _scan.Client;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "";

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    [ObservableProperty]
    public partial SnapshotView? View { get; set; }

    /// <summary>Drivers page filter (see <see cref="Filters"/>).</summary>
    [ObservableProperty]
    public partial string StatusFilter { get; set; } = Filters.AllMatched;

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowInboxAndUnmatched { get; set; }

    public bool HasError => ErrorText.Length > 0;

    public bool HasView => View is not null;

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnViewChanged(SnapshotView? value) => OnPropertyChanged(nameof(HasView));

    partial void OnShowInboxAndUnmatchedChanged(bool value)
    {
        Settings.ShowInboxAndUnmatched = value;
        TrySaveSettings();
        if (!value && Filters.NeedsAllDevices(StatusFilter))
            StatusFilter = Filters.AllMatched;
    }

    partial void OnStatusFilterChanged(string value)
    {
        if (Filters.NeedsAllDevices(value))
            ShowInboxAndUnmatched = true;
    }

    /// <summary>A newer release the user hasn't dismissed; null otherwise.</summary>
    [ObservableProperty]
    public partial AvailableUpdate? Update { get; set; }

    public Task LoadAsync() => ScanAsync(revalidate: false);

    /// <summary>
    /// Run with each scan; the checker asks GitHub at most once a day. <paramref name="force"/> asks now and shows
    /// a release even if its notice was closed (Settings › Check now). Returns the newer release, if any.
    /// </summary>
    public async Task<AvailableUpdate?> CheckForUpdateAsync(bool force = false)
    {
        if (!force && !Settings.CheckForUpdates)
            return null;
        var current = typeof(MainViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0);
        var checker = new UpdateChecker(_http, AppSettings.DataDirectory, current);
        AvailableUpdate? update;
        try
        {
            update = await Task.Run(() => checker.CheckAsync(App.IsPackaged, RuntimeInformation.ProcessArchitecture, force, CancellationToken.None));
        }
        catch (Exception ex)
        {
            App.LogError("Update check failed", ex);
            return null;
        }
        if (force && update is not null && Settings.DismissedUpdate == update.Version.ToString(3))
        {
            Settings.DismissedUpdate = null;
            TrySaveSettings();
        }
        Update = update is not null && Settings.DismissedUpdate != update.Version.ToString(3) ? update : null;
        return update;
    }

    /// <summary>The user closed the notice: don't show this release again.</summary>
    public void DismissUpdate()
    {
        if (Update is null)
            return;
        Settings.DismissedUpdate = Update.Version.ToString(3);
        TrySaveSettings();
        Update = null;
    }

    /// <summary>
    /// Set after a graphics driver reset: the window can't draw new text any more, so "Check again" restarts the
    /// app (which scans on start) instead of scanning into it.
    /// </summary>
    public Action? RestartInsteadOfRefresh { get; set; }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync()
    {
        if (RestartInsteadOfRefresh is { } restart)
        {
            restart();
            return Task.CompletedTask;
        }
        return ScanAsync(revalidate: true);
    }

    private bool CanRefresh() => !IsBusy;

    private async Task ScanAsync(bool revalidate)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsBusy = true;
        ErrorText = "";
        var progress = new Progress<string>(p => ProgressText = p);
        try
        {
            var snapshot = await Task.Run(() => _scan.RunAsync(revalidate, Settings.BoardOverrideId, progress, ct, Settings.CardOverrides), ct);
            _windowsUpdate = Settings.CheckWindowsUpdate ? WindowsUpdateInfo.Searching : WindowsUpdateInfo.Off;
            View = new SnapshotView(snapshot, _windowsUpdate);
            if (Settings.CheckWindowsUpdate)
                _ = SearchWindowsUpdateAsync(ct);
            _ = CheckForUpdateAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorText = ex switch
            {
                HttpRequestException or TaskCanceledException =>
                    $"Couldn't download WinDriverIndex data and there is no saved copy yet: {ex.Message}",
                _ => $"Scan failed: {ex.Message}",
            };
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsBusy = false;
                ProgressText = "";
            }
        }
    }

    /// <summary>
    /// Asks Windows Update which drivers it offers this machine. It's slow (seconds to a minute), so it runs after the
    /// scan and the results are merged into the view when they arrive; a newer scan cancels an older search.
    /// </summary>
    private async Task SearchWindowsUpdateAsync(CancellationToken ct)
    {
        var result = await Task.Run(() => _windowsUpdateSource.SearchAsync(ct), ct).ConfigureAwait(true);
        if (ct.IsCancellationRequested || View is null)
            return;
        _windowsUpdate = WindowsUpdateInfo.From(result);
        View = new SnapshotView(View.Snapshot, _windowsUpdate);
    }

    /// <summary>
    /// Opens Settings › Windows Update › Optional updates when every offer is optional; otherwise the main Windows
    /// Update page, where automatic driver updates are listed and installed.
    /// </summary>
    public static Task OpenWindowsUpdateAsync(bool optionalOnly) =>
        Windows.System.Launcher.LaunchUriAsync(new Uri(optionalOnly
            ? "ms-settings:windowsupdate-optionalupdates"
            : "ms-settings:windowsupdate")).AsTask();

    /// <summary>Use a specific catalogue entry for this PC (persisted), or null to go back to automatic matching.</summary>
    public async Task SelectBoardAsync(Board? board)
    {
        if (View is null)
            return;
        var snapshot = View.Snapshot;
        var isOverride = board is not null;
        if (board is null)
        {
            // Back to automatic matching.
            board = WdiCheck.Core.Matching.BoardMatcher.AutoSelect(snapshot.BoardCandidates)?.Board;
        }

        Settings.BoardOverrideId = isOverride ? board!.BoardId : null;
        TrySaveSettings();

        IsBusy = true;
        ProgressText = board is null ? "" : $"Loading report for {board.Name}…";
        try
        {
            var updated = await Task.Run(() => _scan.WithBoardAsync(snapshot, board, isOverride, revalidate: false, CancellationToken.None));
            View = new SnapshotView(updated, _windowsUpdate);
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    /// <summary>Use a specific catalogue card for the card with this key (persisted), or null for automatic matching.</summary>
    public async Task SelectCardAsync(string key, Board? card)
    {
        if (View is null)
            return;
        if (card is null)
            Settings.CardOverrides.Remove(key);
        else
            Settings.CardOverrides[key] = card.BoardId;
        TrySaveSettings();

        IsBusy = true;
        ProgressText = card is null ? "" : $"Loading report for {card.Name}…";
        try
        {
            var updated = await Task.Run(() => _scan.WithCardAsync(View.Snapshot, key, card, CancellationToken.None));
            View = new SnapshotView(updated, _windowsUpdate);
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    public IEnumerable<Board> SearchBoards(string text) => Search(text, cards: false);

    public IEnumerable<Board> SearchCards(string text) => Search(text, cards: true);

    private IEnumerable<Board> Search(string text, bool cards)
    {
        if (View is null || string.IsNullOrWhiteSpace(text))
            return [];
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return View.Snapshot.Boards
            .Where(b => string.Equals(b.ProductType, WdiCheck.Core.Matching.CardMatcher.ProductType, StringComparison.OrdinalIgnoreCase) == cards)
            .Where(b => words.All(w => (b.Vendor + " " + b.Name + " " + b.Chipset + " " + b.VendorProductId).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Take(25);
    }

    public async Task ExportAsync(bool json, nint hwnd)
    {
        if (View is null)
            return;
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"driver-status-{Environment.MachineName}-{DateTime.Now:yyyyMMdd}",
        };
        picker.FileTypeChoices.Add(json ? "JSON" : "CSV (Excel)", [json ? ".json" : ".csv"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
            return;

        try
        {
            var snapshot = View.Snapshot;
            await using var stream = await file.OpenStreamForWriteAsync();
            stream.SetLength(0);
            if (json)
                await ReportExporter.WriteJsonAsync(stream, snapshot.Results, snapshot.Identity, snapshot.Board, snapshot.Freshness.IndexGenerated,
                    windowsUpdate: _windowsUpdate.Status == WindowsUpdateStatus.Done ? _windowsUpdate.Offers : null);
            else
                await ReportExporter.WriteCsvAsync(stream, snapshot.Results,
                    windowsUpdate: _windowsUpdate.Status == WindowsUpdateStatus.Done ? _windowsUpdate.Offers : null, identity: snapshot.Identity);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText = $"Export failed: {ex.Message}";
        }
    }

    public string? ApplyBaseUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return "Enter an https:// URL, e.g. https://windriverindex.tucny.com/v1/latest/";
        Settings.BaseUrl = uri.AbsoluteUri == WdiClient.DefaultBaseUri.AbsoluteUri ? null : uri.AbsoluteUri;
        TrySaveSettings();
        _scan = CreateScanService();
        _ = ScanAsync(revalidate: true);
        return null;
    }

    public void ClearCache()
    {
        Client.ClearCache();
    }

    public void TrySaveSettings()
    {
        try
        {
            Settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText = $"Couldn't save settings: {ex.Message}";
        }
    }

    private ScanService CreateScanService()
    {
        var baseUri = Settings.BaseUrl is { } u && Uri.TryCreate(u, UriKind.Absolute, out var parsed) ? parsed : null;
        var client = new WdiClient(_http, AppSettings.CacheDirectory, baseUri);
        return new ScanService(client, new DeviceInventory());
    }
}
