using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pos.Desktop;

public partial class SaleConfirmationDialog : Window
{
    public SaleConfirmationDialog()
    {
        InitializeComponent();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
}
