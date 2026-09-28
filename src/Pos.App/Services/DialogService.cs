using System.Windows;

namespace Pos.App.Services;

public interface IDialogService
{
    bool Confirm(string message, string title);
    void Error(string message, string title = "오류");
    void Info(string message, string title = "Simple POS");
}

public sealed class DialogService : IDialogService
{
    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Error(string message, string title = "오류") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void Info(string message, string title = "Simple POS") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
