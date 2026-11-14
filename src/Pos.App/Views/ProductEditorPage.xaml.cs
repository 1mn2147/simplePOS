using System.Windows;
using System.Windows.Controls;
using Pos.App.Services;

namespace Pos.App.Views;

public partial class ProductEditorPage : UserControl
{
    public ProductEditorPage()
    {
        InitializeComponent();
        VirtualInputFocus.Attach(this);
        Loaded += (_, _) => ApplyResponsiveLayout(ActualWidth);
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        if (EditorSecondaryPanel is null) return;
        var narrow = width < 1000;
        EditorColumnGap.Width = new GridLength(narrow ? 0 : 24);
        EditorSecondaryColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(EditorSecondaryPanel, narrow ? 0 : 2);
        Grid.SetRow(EditorSecondaryPanel, narrow ? 1 : 0);
        EditorSecondaryPanel.Margin = new Thickness(0, narrow ? 20 : 0, 0, 0);
    }
}
