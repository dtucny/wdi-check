using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WdiCheck.App.Views;

/// <summary>A board's or card's vendor listing next to what's installed; see the XAML for the layout.</summary>
public sealed partial class ListingTable : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(object), typeof(ListingTable),
            new PropertyMetadata(null, (d, e) => ((ListingTable)d).Rows.ItemsSource = e.NewValue));

    public static readonly DependencyProperty VendorProperty =
        DependencyProperty.Register(nameof(Vendor), typeof(string), typeof(ListingTable),
            new PropertyMetadata("", (d, e) => ((ListingTable)d).VendorHeader.Text =
                $"{(string.IsNullOrEmpty(e.NewValue as string) ? "VENDOR" : ((string)e.NewValue).ToUpperInvariant())} LISTS"));

    public ListingTable()
    {
        InitializeComponent();
    }

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>"MSI": names the middle column ("MSI LISTS").</summary>
    public string Vendor
    {
        get => (string)GetValue(VendorProperty);
        set => SetValue(VendorProperty, value);
    }

    // A horizontally scrolling ScrollViewer measures its content with unlimited width, so star columns can't
    // size themselves; pinning the table to the viewport width lets them fill the card, and MinWidth scrolls when narrow.
    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e) =>
        Table.Width = Math.Max(Table.MinWidth, e.NewSize.Width);
}
