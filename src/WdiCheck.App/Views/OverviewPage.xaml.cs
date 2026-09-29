using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WdiCheck.App.ViewModels;

namespace WdiCheck.App.Views;

public sealed partial class OverviewPage : Page
{
    public OverviewPage()
    {
        InitializeComponent();
    }

    public MainViewModel ViewModel => App.ViewModel;

    private async void ExportCsv_Click(object sender, RoutedEventArgs e) => await ViewModel.ExportAsync(json: false, App.Window.Hwnd);

    private async void ExportJson_Click(object sender, RoutedEventArgs e) => await ViewModel.ExportAsync(json: true, App.Window.Hwnd);

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string filter })
        {
            ViewModel.SearchText = "";
            ViewModel.StatusFilter = filter;
            App.Window.NavigateTo("drivers");
        }
    }

    private void Attention_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
        {
            ViewModel.StatusFilter = Filters.AllMatched;
            ViewModel.SearchText = name;
            App.Window.NavigateTo("drivers");
        }
    }

    private void Board_Click(object sender, RoutedEventArgs e) => App.Window.NavigateTo("board");

    private async void OpenOptionalUpdates_Click(object sender, RoutedEventArgs e) => await MainViewModel.OpenOptionalUpdatesAsync();
}
