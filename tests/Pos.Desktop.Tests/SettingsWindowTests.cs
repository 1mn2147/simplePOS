using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ClosedXML.Excel;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.Desktop.Tests;

public sealed class SettingsWindowTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(), "SimplePOS.Settings.Tests", Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public async Task ApplyValidSettingsAndProtectDatabaseWhileCartHasItems()
    {
        var currentDatabase = await CreateDatabaseAsync("current.xlsx");
        var otherDatabase = await CreateDatabaseAsync("other.xlsx");
        var current = new AppSettings
        {
            DatabasePath = currentDatabase,
            BackupDirectory = Path.Combine(_temporaryDirectory, "backups"),
        };
        var window = new SettingsWindow(current, cartHasItems: true);
        window.FindControl<TextBox>("DatabasePathInput")!.Text = otherDatabase;

        Assert.Null(await window.TryApplyAsync());
        Assert.True(window.FindControl<Control>("CartDatabaseWarning")!.IsVisible);
        Assert.Contains("장바구니", window.FindControl<TextBlock>("SettingsStatusText")!.Text);

        window.FindControl<TextBox>("DatabasePathInput")!.Text = currentDatabase;
        window.FindControl<ComboBox>("BackupIntervalInput")!.SelectedIndex = 2;
        window.FindControl<TextBox>("BackupTimeInput")!.Text = "04:30";
        window.FindControl<NumericUpDown>("BackupRetentionInput")!.Value = 12;
        window.FindControl<ComboBox>("OutOfStockPolicyInput")!.SelectedIndex = 1;
        window.FindControl<CheckBox>("ScannerSoundInput")!.IsChecked = false;
        window.FindControl<CheckBox>("StartFullscreenInput")!.IsChecked = false;
        window.FindControl<CheckBox>("RunAtLoginInput")!.IsChecked = true;
        window.FindControl<ComboBox>("ScannerTerminatorInput")!.SelectedIndex = 1;

        var result = await window.TryApplyAsync();

        Assert.NotNull(result);
        Assert.Equal(7, result.BackupIntervalDays);
        Assert.Equal(new TimeOnly(4, 30), result.BackupTime);
        Assert.Equal(12, result.BackupRetentionCount);
        Assert.Equal(OutOfStockPolicy.WarnAndAllow, result.OutOfStockPolicy);
        Assert.False(result.ScannerSuccessSoundEnabled);
        Assert.False(result.StartFullScreen);
        Assert.True(result.RunAtStartup);
        Assert.Equal("Tab", result.ScannerTerminatorKey);
    }

    [AvaloniaFact]
    public async Task CustomBackupIntervalIsPreservedAndUnsavedCloseNeedsConfirmation()
    {
        var database = await CreateDatabaseAsync("custom-interval.xlsx");
        var window = new SettingsWindow(new AppSettings
        {
            DatabasePath = database,
            BackupDirectory = Path.Combine(_temporaryDirectory, "backups"),
            BackupIntervalDays = 11,
        }, cartHasItems: false);
        window.Show();

        var settings = await window.TryApplyAsync();
        Assert.NotNull(settings);
        Assert.Equal(11, settings.BackupIntervalDays);

        window.FindControl<TextBox>("BackupTimeInput")!.Text = "05:00";
        window.Close();
        Assert.True(window.FindControl<Control>("DiscardChangesPanel")!.IsVisible);

        window.FindControl<Button>("DiscardChangesButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs
        {
            RoutedEvent = Button.ClickEvent,
        });
    }

    [AvaloniaFact]
    public async Task LegacyMigrationCreatesPosCopyAndPreservesSource()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var source = Path.Combine(_temporaryDirectory, "기존 상품.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("상품");
            sheet.Cell("A1").Value = "상품명";
            sheet.Cell("B1").Value = "가격";
            sheet.Cell("C1").Value = "바코드";
            sheet.Cell("A2").Value = "보리차";
            sheet.Cell("B2").Value = 1500;
            sheet.Cell("C2").Value = "8800000000001";
            workbook.SaveAs(source);
        }
        var hash = await HashAsync(source);
        var window = new SettingsWindow(new AppSettings
        {
            DatabasePath = source,
            BackupDirectory = Path.Combine(_temporaryDirectory, "backups"),
        }, cartHasItems: false);

        var migration = await window.MigrateSelectedDatabaseAsync();

        Assert.NotNull(migration);
        Assert.Equal(hash, await HashAsync(source));
        Assert.Equal(Path.Combine(_temporaryDirectory, "기존 상품_pos.xlsx"), migration.DestinationPath);
        Assert.True(File.Exists(migration.DestinationPath));
        Assert.Equal("보리차", Assert.Single(await new ExcelPosRepository(migration.DestinationPath).GetProductsAsync()).Name);
        Assert.Equal(migration.DestinationPath, window.FindControl<TextBox>("DatabasePathInput")!.Text);
    }

    [AvaloniaFact]
    public async Task MigrationIsBlockedWhileCartHasItems()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var source = Path.Combine(_temporaryDirectory, "legacy-blocked.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("상품");
            sheet.Cell("A1").Value = "상품명";
            sheet.Cell("B1").Value = "가격";
            sheet.Cell("C1").Value = "바코드";
            sheet.Cell("A2").Value = "물";
            sheet.Cell("B2").Value = 1000;
            sheet.Cell("C2").Value = "123";
            workbook.SaveAs(source);
        }
        var window = new SettingsWindow(new AppSettings
        {
            DatabasePath = source,
            BackupDirectory = Path.Combine(_temporaryDirectory, "backups"),
        }, cartHasItems: true);

        Assert.Null(await window.MigrateSelectedDatabaseAsync());
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "legacy-blocked_pos.xlsx")));
        Assert.Contains("장바구니", window.FindControl<TextBlock>("SettingsStatusText")!.Text);
    }

    [AvaloniaFact]
    public async Task ManualBackupCreatesReopenableManagedCopy()
    {
        var database = await CreateDatabaseAsync("products.xlsx");
        var backupDirectory = Path.Combine(_temporaryDirectory, "backups");
        var window = new SettingsWindow(new AppSettings
        {
            DatabasePath = database,
            BackupDirectory = backupDirectory,
            BackupRetentionCount = 3,
        }, cartHasItems: false);

        var result = await window.CreateBackupAsync();

        Assert.NotNull(result);
        Assert.True(File.Exists(result.Path));
        Assert.True(await new BackupService().ValidateAsync(result.Path));
        Assert.Contains(Path.GetFileName(result.Path), window.FindControl<TextBlock>("BackupStatusText")!.Text);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task<string> CreateDatabaseAsync(string name)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, name);
        await new ExcelPosRepository(path).CreateAsync();
        return path;
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
}
