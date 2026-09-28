using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.App.Views;

public partial class ProductEditorPage : UserControl
{
    public ProductEditorPage()
    {
        InitializeComponent();
    }

    private void OnKoreanInputGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ExecuteDataContextCommand("ShowKoreanKeyboardCommand", (sender as FrameworkElement)?.Tag);
    }

    private void OnNumericInputGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ExecuteDataContextCommand("ShowNumericKeypadCommand", (sender as FrameworkElement)?.Tag);
    }

    private void ExecuteDataContextCommand(string propertyName, object? parameter)
    {
        var commandProperty = DataContext?.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);

        if (commandProperty?.GetValue(DataContext) is ICommand command && command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }
}
