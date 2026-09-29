using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WdiCheck.App.ViewModels;

namespace WdiCheck.App.Views;

public sealed partial class DevicesPage : Page, INotifyPropertyChanged
{
    private bool _syncing;

    public DevicesPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel ViewModel => App.ViewModel;

    public DeviceRowViewModel? Selected { get; private set; }

    public bool HasSelection => Selected is not null;

    public bool IsEmpty { get; private set; }

    public string EmptyText { get; private set; } = "";

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        _syncing = true;
        SearchBox.Text = ViewModel.SearchText;
        FilterBox.SelectedItem = FilterBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == ViewModel.StatusFilter)
                                 ?? FilterBox.Items[0];
        _syncing = false;
        Rebuild(selectFirst: !string.IsNullOrEmpty(ViewModel.SearchText));
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.View):
            case nameof(MainViewModel.ShowInboxAndUnmatched):
                Rebuild(selectFirst: false);
                break;
            case nameof(MainViewModel.StatusFilter):
                _syncing = true;
                FilterBox.SelectedItem = FilterBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == ViewModel.StatusFilter);
                _syncing = false;
                Rebuild(selectFirst: false);
                break;
        }
    }

    private void Rebuild(bool selectFirst)
    {
        var view = ViewModel.View;
        var previous = Selected?.Result.Device.InstanceId;
        if (view is null)
        {
            GroupedRows.Source = new List<DeviceGroup>();
            SetEmpty(true, ViewModel.IsBusy ? "Checking drivers…" : "No scan yet.");
            return;
        }

        var filter = ViewModel.StatusFilter;
        var showAll = ViewModel.ShowInboxAndUnmatched;
        var query = ViewModel.SearchText.Trim();
        var rows = view.Rows
            .Where(r => Filters.Accept(filter, r.Result))
            .Where(r => showAll || r.Result.Status is not (Core.Matching.DriverStatus.Inbox or Core.Matching.DriverStatus.NotIndexed))
            .Where(r => r.Matches(query))
            .ToList();

        var groups = rows
            .GroupBy(r => r.GroupKey)
            .Select(g => new DeviceGroup(g.Key, g.First().GroupName, g))
            .OrderBy(g => g.Key.StartsWith('~'))
            .ThenBy(g => g.Min(r => r.SortKey))
            .ThenBy(g => g.Header, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        GroupedRows.Source = groups;

        SetEmpty(rows.Count == 0, query.Length > 0 ? $"Nothing matches “{query}”." : "No drivers in this category.");

        var reselect = rows.FirstOrDefault(r => r.Result.Device.InstanceId == previous)
                       ?? (selectFirst ? rows.FirstOrDefault() : null);
        DeviceList.SelectedItem = reselect;
        if (reselect is null)
            SetSelected(null);
        else
            DeviceList.ScrollIntoView(reselect);
    }

    private void SetEmpty(bool empty, string text)
    {
        IsEmpty = empty;
        EmptyText = text;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void SetSelected(DeviceRowViewModel? row)
    {
        Selected = row;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SetSelected(DeviceList.SelectedItem as DeviceRowViewModel);

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_syncing || args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;
        ViewModel.SearchText = sender.Text;
        Rebuild(selectFirst: false);
    }

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || FilterBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;
        ViewModel.StatusFilter = tag;
    }

    private async void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            await Windows.System.Launcher.LaunchUriAsync(uri);
    }

    /// <summary>
    /// List width the column layout needs: a usable device/family column (~200) plus three 150 px columns,
    /// spacing and item/scrollbar padding. Narrower lists use the compact rows (installed → newest under the name).
    /// </summary>
    private const double ColumnLayoutMinWidth = 740;

    private bool? _columnLayout;

    // Driven by the list's own width, not the window's: the navigation pane and the detail pane's position
    // both change how much room the list gets at a given window width.
    private void ListCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var columns = e.NewSize.Width >= ColumnLayoutMinWidth;
        if (_columnLayout == columns)
            return;
        _columnLayout = columns;
        DeviceList.ItemTemplate = (DataTemplate)Resources[columns ? "WideRowTemplate" : "NarrowRowTemplate"];
        VersionColumns.Visibility = columns ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OpenOptionalUpdates_Click(object sender, RoutedEventArgs e) => await MainViewModel.OpenOptionalUpdatesAsync();

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
