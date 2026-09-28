using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WdiCheck.Core.Api;
using WdiCheck.Core.Inventory;
using WdiCheck.Core.Reporting;
using WdiCheck.Core.Scanning;
using WdiCheck.Core.Settings;
using Windows.Storage.Pickers;

namespace WdiCheck.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly HttpClient _http = WdiClient.CreateHttpClient();
    private ScanService _scan;
    private CancellationTokenSource? _cts;

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

    public Task LoadAsync() => ScanAsync(revalidate: false);

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => ScanAsync(revalidate: true);

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
            var snapshot = await Task.Run(() => _scan.RunAsync(revalidate, Settings.BoardOverrideId, progress, ct), ct);
            View = new SnapshotView(snapshot);
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
            View = new SnapshotView(updated);
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    public IEnumerable<Board> SearchBoards(string text)
    {
        if (View is null || string.IsNullOrWhiteSpace(text))
            return [];
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return View.Snapshot.Boards
            .Where(b => !string.Equals(b.ProductType, "graphics-card", StringComparison.OrdinalIgnoreCase))
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
                await ReportExporter.WriteJsonAsync(stream, snapshot.Results, snapshot.Identity, snapshot.Board, snapshot.Freshness.IndexGenerated);
            else
                await ReportExporter.WriteCsvAsync(stream, snapshot.Results);
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
