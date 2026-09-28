using System.Windows;
using System.Windows.Controls;

namespace Pos.App.Views;

public partial class MainPage : UserControl
{
    private const double HorizontalWorkspaceBreakpoint = 1200;

    public MainPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyResponsiveLayout(ActualWidth);
    }

    private void OnMainPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        var useHorizontalLayout = width >= HorizontalWorkspaceBreakpoint;

        WorkspacePrimaryRow.Height = useHorizontalLayout
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(3, GridUnitType.Star);
        WorkspaceSecondaryRow.Height = useHorizontalLayout
            ? new GridLength(0)
            : new GridLength(2, GridUnitType.Star);

        WorkspacePrimaryColumn.Width = useHorizontalLayout
            ? new GridLength(3, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        WorkspaceSecondaryColumn.Width = useHorizontalLayout
            ? new GridLength(2, GridUnitType.Star)
            : new GridLength(0);

        Grid.SetRow(CartPanel, 0);
        Grid.SetColumn(CartPanel, 0);
        Grid.SetRow(QuickAddPanel, useHorizontalLayout ? 0 : 1);
        Grid.SetColumn(QuickAddPanel, useHorizontalLayout ? 1 : 0);

        CartPanel.Margin = useHorizontalLayout
            ? new Thickness(0, 0, 8, 0)
            : new Thickness(0, 0, 0, 8);
        QuickAddPanel.Margin = useHorizontalLayout
            ? new Thickness(8, 0, 0, 0)
            : new Thickness(0, 8, 0, 0);

        DesktopCheckoutPanel.Visibility = useHorizontalLayout
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompactCheckoutPanel.Visibility = useHorizontalLayout
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
