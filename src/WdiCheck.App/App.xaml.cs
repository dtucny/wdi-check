using Microsoft.UI.Xaml;
using WdiCheck.App.ViewModels;
using WdiCheck.Core.Settings;

namespace WdiCheck.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            LogError("Unhandled exception", e.Exception);
            if (ViewModel is not null)
            {
                ViewModel.ErrorText = $"Unexpected error: {e.Exception.Message} (details in {LogPath})";
                e.Handled = true;
            }
        };
    }

    public static MainViewModel ViewModel { get; private set; } = null!;

    public static MainWindow Window { get; private set; } = null!;

    /// <summary>Installed from the MSIX rather than run from the zip.</summary>
    public static bool IsPackaged { get; } = GetIsPackaged();

    private static bool GetIsPackaged()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static string LogPath => Path.Combine(AppSettings.DataDirectory, "error.log");

    public static void LogError(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {context}: {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ViewModel = new MainViewModel(AppSettings.Load());
        Window = new MainWindow();
        Window.Activate();
        _ = ViewModel.LoadAsync();
    }
}
