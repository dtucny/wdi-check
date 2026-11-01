using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WdiCheck.App.ViewModels;
using WdiCheck.Core.Api;

namespace WdiCheck.App.Views;

/// <summary>One add-in graphics card; navigated to with the card's key (<see cref="CardView.Key"/>).</summary>
public sealed partial class CardPage : Page
{
    private string? _key;

    public CardPage()
    {
        InitializeComponent();
    }

    public MainViewModel ViewModel => App.ViewModel;

    public CardView? Card => _key is null ? null : ViewModel.View?.Cards.FirstOrDefault(c => c.Key == _key);

    public bool HasCard => Card is not null;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _key = e.Parameter as string;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Bindings.Update();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

    // Card and HasCard are computed from the view; a new scan or a choice of model replaces it.
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.View))
            Bindings.Update();
    }

    private void CardSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = ViewModel.SearchCards(sender.Text).ToList();
    }

    private async void CardSearch_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is Board board && _key is not null)
        {
            sender.Text = "";
            sender.ItemsSource = null;
            await ViewModel.SelectCardAsync(_key, board);
        }
    }

    private async void Candidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Board board } && _key is not null)
            await ViewModel.SelectCardAsync(_key, board);
    }

    private async void Auto_Click(object sender, RoutedEventArgs e)
    {
        if (_key is not null)
            await ViewModel.SelectCardAsync(_key, null);
    }
}
