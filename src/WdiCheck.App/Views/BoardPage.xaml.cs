using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WdiCheck.App.ViewModels;
using WdiCheck.Core.Api;

namespace WdiCheck.App.Views;

public sealed partial class BoardPage : Page
{
    public BoardPage()
    {
        InitializeComponent();
    }

    public MainViewModel ViewModel => App.ViewModel;

    private void BoardSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = ViewModel.SearchBoards(sender.Text).ToList();
    }

    private async void BoardSearch_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is Board board)
        {
            sender.Text = "";
            sender.ItemsSource = null;
            await ViewModel.SelectBoardAsync(board);
        }
    }

    private async void Candidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Board board })
            await ViewModel.SelectBoardAsync(board);
    }

    private async void Auto_Click(object sender, RoutedEventArgs e) => await ViewModel.SelectBoardAsync(null);
}
