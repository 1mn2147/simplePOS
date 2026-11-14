using System.Windows;
using System.Windows.Controls;
using Pos.App.Services;

namespace Pos.App.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        VirtualInputFocus.Attach(this);
        Loaded += (_, _) => ApplyResponsiveLayout(ActualWidth);
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        if (SettingsSecondaryPanel is null) return;
        var narrow = width < 1000;
        SettingsColumnGap.Width = new GridLength(narrow ? 0 : 20);
        SettingsSecondaryColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(SettingsSecondaryPanel, narrow ? 0 : 2);
        Grid.SetRow(SettingsSecondaryPanel, narrow ? 1 : 0);
        SettingsSecondaryPanel.Margin = new Thickness(0, narrow ? 20 : 0, 0, 0);
    }
}
