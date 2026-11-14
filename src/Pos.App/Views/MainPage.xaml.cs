using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pos.App.ViewModels;

namespace Pos.App.Views;

public partial class MainPage : UserControl
{
    public MainPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ObserveMainPage(DataContext as MainPageViewModel);
            ApplyResponsiveLayout(ActualWidth);
        };
        Unloaded += (_, _) => ObserveMainPage(null);
        DataContextChanged += (_, _) =>
        {
            if (IsLoaded) ObserveMainPage(DataContext as MainPageViewModel);
        };
    }

    private MainPageViewModel? _observedMainPage;

    private void ObserveMainPage(MainPageViewModel? main)
    {
        if (_observedMainPage is not null) _observedMainPage.PropertyChanged -= OnMainPagePropertyChanged;
        _observedMainPage = main;
        if (main is not null) main.PropertyChanged += OnMainPagePropertyChanged;
    }

    private void OnMainPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainPageViewModel.RecentProductId) or nameof(MainPageViewModel.RecentProductQuantity))) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!IsLoaded || _observedMainPage is null) return;
            var recent = _observedMainPage.CartItems.FirstOrDefault(item => item.IsRecentlyAdded);
            if (recent is not null) CartList.ScrollIntoView(recent);
        });
    }

    private void OnMainPageSizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var wide = width >= 1200;
        WorkspacePrimaryRow.Height = new GridLength(1, GridUnitType.Star);
        WorkspaceSecondaryRow.Height = new GridLength(0);
        WorkspacePrimaryColumn.Width = new GridLength(wide ? 3 : 2.1, GridUnitType.Star);
        WorkspaceSecondaryColumn.Width = new GridLength(wide ? 2 : 1, GridUnitType.Star);
        Grid.SetRow(QuickAddPanel, 0);
        Grid.SetColumn(QuickAddPanel, 1);
        if (DataContext is MainPageViewModel main) main.QuickAddColumns = wide ? 4 : width >= 1050 ? 3 : 2;
        DesktopCheckoutPanel.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        CompactCheckoutPanel.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
    }
}
