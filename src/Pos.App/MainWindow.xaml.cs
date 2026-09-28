using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Pos.App.ViewModels;
using Pos.Core;

namespace Pos.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (DataContext is not ShellViewModel shell || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var status = BarcodeInputStatus.Ignored;
        foreach (var character in e.Text)
        {
            status = await shell.HandleScannerKeyAsync(
                character.ToString(),
                DateTimeOffset.UtcNow,
                IsTextEntryActive());
        }

        e.Handled = status is BarcodeInputStatus.Buffering or BarcodeInputStatus.Completed;
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ShellViewModel shell || e.Key is not (Key.Enter or Key.Tab))
        {
            return;
        }

        var status = await shell.HandleScannerKeyAsync(
            e.Key == Key.Enter ? "Enter" : "Tab",
            DateTimeOffset.UtcNow,
            IsTextEntryActive());
        e.Handled = status is BarcodeInputStatus.Completed or BarcodeInputStatus.Rejected;
    }

    private static bool IsTextEntryActive() =>
        Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox;
}
