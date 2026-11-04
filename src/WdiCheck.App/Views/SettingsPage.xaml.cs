using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WdiCheck.App.ViewModels;
using WdiCheck.Core.Api;

namespace WdiCheck.App.Views;

public sealed partial class SettingsPage : Page
{
    private static readonly string[] Themes = ["Default", "Light", "Dark"];
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        ThemeButtons.SelectedIndex = Math.Max(0, Array.IndexOf(Themes, ViewModel.Settings.Theme));
        BaseUrlBox.Text = ViewModel.Client.BaseUri.AbsoluteUri;
        WindowsUpdateToggle.IsOn = ViewModel.Settings.CheckWindowsUpdate;
        UpdatesToggle.IsOn = ViewModel.Settings.CheckForUpdates;
        VersionText.Text = $"WdiCheck {typeof(App).Assembly.GetName().Version?.ToString(3)}";
        UpdateCacheText();
        _loading = false;
    }

    public MainViewModel ViewModel => App.ViewModel;

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeButtons.SelectedIndex < 0)
            return;
        ViewModel.Settings.Theme = Themes[ThemeButtons.SelectedIndex];
        ViewModel.TrySaveSettings();
        App.Window.ApplyTheme(ViewModel.Settings.Theme);
    }

    private void WindowsUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        ViewModel.Settings.CheckWindowsUpdate = WindowsUpdateToggle.IsOn;
        ViewModel.TrySaveSettings();
    }

    private void UpdatesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        ViewModel.Settings.CheckForUpdates = UpdatesToggle.IsOn;
        ViewModel.TrySaveSettings();
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        CheckNowButton.IsEnabled = false;
        CheckNowText.Text = "Checking…";
        var update = await ViewModel.CheckForUpdateAsync(force: true);
        CheckNowText.Text = update is null
            ? "No newer version found."
            : $"WdiCheck {update.Version.ToString(3)} is available: see the menu, above Settings.";
        CheckNowButton.IsEnabled = true;
    }

    private void ApplyUrl_Click(object sender, RoutedEventArgs e)
    {
        var error = ViewModel.ApplyBaseUrl(BaseUrlBox.Text);
        UrlError.Text = error ?? "";
        UrlError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateCacheText();
    }

    private void ResetUrl_Click(object sender, RoutedEventArgs e)
    {
        BaseUrlBox.Text = WdiClient.DefaultBaseUri.AbsoluteUri;
        ApplyUrl_Click(sender, e);
    }

    private async void OpenCache_Click(object sender, RoutedEventArgs e)
    {
        var dir = ViewModel.Client.CacheDirectory;
        Directory.CreateDirectory(dir);
        await Windows.System.Launcher.LaunchFolderPathAsync(dir);
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.ClearCache();
        }
        catch (IOException ex)
        {
            CacheText.Text = $"Couldn't clear the cache: {ex.Message}";
            return;
        }

        UpdateCacheText();
    }

    private void UpdateCacheText()
    {
        var size = ViewModel.Client.CacheSizeBytes();
        CacheText.Text = $"{ViewModel.Client.CacheDirectory} — {(size / 1024.0 / 1024.0).ToString("0.0", CultureInfo.CurrentCulture)} MB";
    }
}
