using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.Desktop.Tests;

public sealed class SalesManagementTests : IDisposable
{
    private readonly string _temporaryDirectory = Directory
        .CreateTempSubdirectory("SimplePOS.SalesManagement.")
        .FullName;

    [AvaloniaFact]
    public async Task SwitchingDatabaseClearsUndoHistoryFromPreviousCatalog()
    {
        var first = await CreateDatabaseAsync("첫 매장.xlsx", new Product
        {
            Name = "첫 매장 상품",
            Barcode = "FIRST-001",
            Price = 1_000,
        });
        var second = await CreateDatabaseAsync("둘째 매장.xlsx", new Product
        {
            Name = "둘째 매장 상품",
            Barcode = "SECOND-001",
            Price = 2_000,
            QuickAdd = true,
        });
        var backupDirectory = Path.Combine(_temporaryDirectory, "backups");
        var settings = CreateSettings(first.DatabasePath, backupDirectory);
        var window = new SalesWindow(
            first,
            settings,
            updated => Task.FromResult<IPosRepository>(new ExcelPosRepository(
                updated.DatabasePath!, updated.OutOfStockPolicy)),
            static () => { },
            new BackupService());
        await window.InitializeAsync();
        window.Show();

        try
        {
            SubmitBarcode(window, "FIRST-001");
            await WaitForAsync(() => ItemCount(window) == "1개");
            Click(window.FindControl<Button>("ClearCartButton")!);
            Assert.Equal("0개", ItemCount(window));
            Assert.True(window.FindControl<Button>("UndoCartButton")!.IsEnabled);

            var dialog = await OpenSettingsAsync(window);
            dialog.FindControl<TextBox>("DatabasePathInput")!.Text = second.DatabasePath;
            dialog.FindControl<TextBox>("BackupDirectoryInput")!.Text = backupDirectory;
            Click(dialog.FindControl<Button>("SaveSettingsButton")!);
            await WaitForAsync(() => !dialog.IsVisible);
            await WaitForAsync(() => window.FindControl<TextBlock>("StatusMessage")!.Text == "설정을 저장했습니다.");

            Assert.Equal("0개", ItemCount(window));
            Assert.False(window.FindControl<Button>("UndoCartButton")!.IsEnabled);
            Click(window.FindControl<Button>("UndoCartButton")!);
            Assert.Equal("0개", ItemCount(window));
            var quickAdd = Assert.Single(window.FindControl<WrapPanel>("QuickAddPanel")!.Children.OfType<Button>());
            Assert.Contains("둘째 매장 상품", quickAdd.Content?.ToString());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SameDatabaseSettingsPreserveCartAndApplyTabScannerTerminator()
    {
        var repository = await CreateDatabaseAsync("같은 매장.xlsx", new Product
        {
            Name = "탭 스캔 상품",
            Barcode = "TAB-001",
            Price = 1_500,
            Stock = 5,
        });
        var backupDirectory = Path.Combine(_temporaryDirectory, "backups-same");
        var settings = CreateSettings(repository.DatabasePath, backupDirectory);
        var window = new SalesWindow(
            repository,
            settings,
            updated => Task.FromResult<IPosRepository>(new ExcelPosRepository(
                updated.DatabasePath!, updated.OutOfStockPolicy)),
            static () => { },
            new BackupService());
        await window.InitializeAsync();
        window.Show();

        try
        {
            SubmitBarcode(window, "TAB-001");
            await WaitForAsync(() => ItemCount(window) == "1개");

            var dialog = await OpenSettingsAsync(window);
            dialog.FindControl<TextBox>("BackupDirectoryInput")!.Text = backupDirectory;
            dialog.FindControl<ComboBox>("ScannerTerminatorInput")!.SelectedIndex = 1;
            dialog.FindControl<CheckBox>("ScannerSoundInput")!.IsChecked = false;
            Click(dialog.FindControl<Button>("SaveSettingsButton")!);
            await WaitForAsync(() => !dialog.IsVisible);
            await WaitForAsync(() => window.FindControl<TextBlock>("StatusMessage")!.Text == "설정을 저장했습니다.");

            Assert.Equal("1개", ItemCount(window));
            RaiseScan(window, "TAB-001", Key.Tab);
            await WaitForAsync(() => ItemCount(window) == "2개");
            Assert.Equal("2개", ItemCount(window));
        }
        finally
        {
            window.Close();
        }
    }

    public void Dispose() => Directory.Delete(_temporaryDirectory, recursive: true);

    private async Task<ExcelPosRepository> CreateDatabaseAsync(string fileName, Product product)
    {
        var path = Path.Combine(_temporaryDirectory, fileName);
        var repository = new ExcelPosRepository(path);
        await repository.CreateAsync();
        await repository.SaveProductAsync(product);
        return repository;
    }

    private static AppSettings CreateSettings(string databasePath, string backupDirectory) => new()
    {
        DatabasePath = databasePath,
        BackupDirectory = backupDirectory,
        ScannerTerminatorKey = "Enter",
        ScannerSuccessSoundEnabled = true,
        OutOfStockPolicy = OutOfStockPolicy.Block,
    };

    private static async Task<SettingsWindow> OpenSettingsAsync(SalesWindow window)
    {
        Click(window.FindControl<Button>("SettingsButton")!);
        await WaitForAsync(() => window.OwnedWindows.OfType<SettingsWindow>().Any(dialog => dialog.IsVisible));
        return window.OwnedWindows.OfType<SettingsWindow>().Single(dialog => dialog.IsVisible);
    }

    private static string ItemCount(SalesWindow window) =>
        window.FindControl<TextBlock>("TotalItemCountText")!.Text!;

    private static void RaiseScan(SalesWindow window, string barcode, Key key)
    {
        var input = window.FindControl<TextBox>("BarcodeInput")!;
        input.Text = barcode;
        input.Focus();
        Dispatcher.UIThread.RunJobs();
        input.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            Source = input,
        });
    }

    private static void SubmitBarcode(SalesWindow window, string barcode)
    {
        window.FindControl<TextBox>("BarcodeInput")!.Text = barcode;
        Click(window.FindControl<Button>("AddBarcodeButton")!);
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 150 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The expected sales management state was not reached.");
    }
}
