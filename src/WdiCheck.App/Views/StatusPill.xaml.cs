using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WdiCheck.App.ViewModels;

namespace WdiCheck.App.Views;

/// <summary>Coloured status chip; colours come from theme resources so they follow light/dark.</summary>
public sealed partial class StatusPill : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusPill),
            new PropertyMetadata("", (d, e) => ((StatusPill)d).Label.Text = e.NewValue as string ?? ""));

    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(Severity), typeof(StatusPill),
            new PropertyMetadata(Severity.Neutral, (d, _) => ((StatusPill)d).UpdateState()));

    public StatusPill()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateState();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Severity Severity
    {
        get => (Severity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    private void UpdateState() => VisualStateManager.GoToState(this, Severity.ToString(), false);
}
