using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using WdiCheck.App.Views;

namespace WdiCheck.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 860));
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = 480;
            p.PreferredMinimumHeight = 480;
        }

        ApplyTheme(App.ViewModel.Settings.Theme);
        RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtons();
        ContentFrame.NavigationFailed += (_, e) =>
        {
            App.LogError($"Navigation to {e.SourcePageType.Name} failed", e.Exception);
            App.ViewModel.ErrorText = $"Couldn't open that page: {e.Exception.Message}";
            e.Handled = true;
        };
        Nav.SelectedItem = Nav.MenuItems[0];
        App.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(App.ViewModel.View))
                UpdateThisPc();
        };

        _gpuWatcher.Reset += () => DispatcherQueue.TryEnqueue(OnGraphicsReset);
        _gpuWatcher.Start();
        Closed += (_, _) => _gpuWatcher.Dispose();
    }

    private readonly GpuResetWatcher _gpuWatcher = new();

    private void OnGraphicsReset()
    {
        GpuResetBar.Visibility = Visibility.Visible;
        GpuResetBar.IsOpen = true;
        _ = RewatchAsync();
    }

    /// <summary>A driver install resets more than once: watch again on a new device once the driver is back.</summary>
    private async Task RewatchAsync()
    {
        for (var attempt = 0; attempt < 24 && !_gpuWatcher.Start(); attempt++)
            await Task.Delay(TimeSpan.FromSeconds(5));
    }

    private void GpuResetBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) => sender.Visibility = Visibility.Collapsed;

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        // Packaged or not, AppInstance restarts the same way; it returns only when the restart failed.
        var reason = Microsoft.Windows.AppLifecycle.AppInstance.Restart("");
        App.LogError("Restart after a graphics reset failed", new InvalidOperationException(reason.ToString()));
        if (Environment.ProcessPath is { } exe)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Exit();
        }
    }

    private const string BoardTag = "board";
    private const string CardTagPrefix = "card:";
    private string _thisPcSignature = "";
    private string? _currentTag;
    private bool _rebuilding;

    public static string CardTag(string key) => CardTagPrefix + key;

    /// <summary>
    /// "This PC" opens the board page; with add-in graphics cards it expands instead, into the board and a page per
    /// card. Rebuilt only when the cards (or the board's kind) change, not on every view update.
    /// </summary>
    private void UpdateThisPc()
    {
        var view = App.ViewModel.View;
        var cards = view?.Cards ?? [];
        var boardTitle = view?.BoardPageTitle ?? "Motherboard";
        var signature = string.Join("|", cards.Select(c => $"{c.Key}={c.NavTitle}").Prepend(boardTitle));
        if (signature == _thisPcSignature)
            return;
        _thisPcSignature = signature;

        var selected = (Nav.SelectedItem as NavigationViewItem)?.Tag as string;
        _rebuilding = true;
        try
        {
            ThisPcItem.MenuItems.Clear();
            if (cards.Count == 0)
            {
                ThisPcItem.Tag = BoardTag;
                ThisPcItem.SelectsOnInvoked = true;
            }
            else
            {
                ThisPcItem.Tag = "this-pc";
                ThisPcItem.SelectsOnInvoked = false;
                ThisPcItem.MenuItems.Add(NavItem(boardTitle, BoardTag, ""));
                foreach (var card in cards)
                    ThisPcItem.MenuItems.Add(NavItem(card.NavTitle, CardTag(card.Key), ""));
                ThisPcItem.IsExpanded = true;
            }
        }
        finally
        {
            _rebuilding = false;
        }

        // Keep the user where they were; a card that's gone falls back to the board page.
        if (selected is BoardTag || selected?.StartsWith(CardTagPrefix, StringComparison.Ordinal) == true)
            NavigateTo(FindItem(selected) is null ? BoardTag : selected);
    }

    private static NavigationViewItem NavItem(string content, string tag, string glyph) =>
        new() { Content = content, Tag = tag, Icon = new FontIcon { Glyph = glyph } };

    private NavigationViewItem? FindItem(string tag) => Nav.MenuItems.OfType<NavigationViewItem>()
        .SelectMany(i => i.MenuItems.OfType<NavigationViewItem>().Prepend(i))
        .FirstOrDefault(i => (i.Tag as string) == tag);

    public nint Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>"Light", "Dark" or anything else for the system default.</summary>
    public void ApplyTheme(string theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateCaptionButtons();
    }

    /// <summary>Navigate to a page by tag ("overview", "drivers", "board", "settings", or <see cref="CardTag"/>).</summary>
    public void NavigateTo(string tag)
    {
        var item = tag == "settings" ? Nav.SettingsItem : FindItem(tag);
        if (item is not null)
            Nav.SelectedItem = item;
    }

    private void UpdateCaptionButtons()
    {
        // Keep the min/max/close glyphs legible against the chosen theme.
        var dark = RootGrid.ActualTheme == ElementTheme.Dark;
        var tb = AppWindow.TitleBar;
        tb.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        tb.ButtonHoverForegroundColor = tb.ButtonForegroundColor;
        tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Rebuilding "This PC" clears the selected child for a moment; UpdateThisPc reselects.
        if (_rebuilding || (!args.IsSettingsSelected && args.SelectedItem is null))
            return;
        var tag = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (tag == _currentTag)
            return;
        _currentTag = tag;
        if (tag?.StartsWith(CardTagPrefix, StringComparison.Ordinal) == true)
        {
            ContentFrame.Navigate(typeof(CardPage), tag[CardTagPrefix.Length..], new EntranceNavigationTransitionInfo());
            return;
        }
        var page = tag switch
        {
            "settings" => typeof(SettingsPage),
            "drivers" => typeof(DevicesPage),
            BoardTag => typeof(BoardPage),
            _ => typeof(OverviewPage),
        };
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }
}
