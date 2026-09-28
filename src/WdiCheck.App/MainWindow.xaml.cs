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
    }

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

    /// <summary>Navigate to a page by tag ("overview", "drivers", "board", "settings").</summary>
    public void NavigateTo(string tag)
    {
        var item = tag == "settings"
            ? Nav.SettingsItem
            : Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
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
        var page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : (args.SelectedItem as NavigationViewItem)?.Tag switch
            {
                "drivers" => typeof(DevicesPage),
                "board" => typeof(BoardPage),
                _ => typeof(OverviewPage),
            };
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }
}
