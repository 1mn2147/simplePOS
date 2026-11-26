using System.Globalization;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.Desktop;

public partial class SettingsWindow : Window
{
    private static readonly FilePickerFileType ExcelFileType = new("Excel 통합 문서")
    {
        Patterns = ["*.xlsx"],
        MimeTypes = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
        AppleUniformTypeIdentifiers = ["org.openxmlformats.spreadsheetml.sheet"],
    };

    private readonly AppSettings _current;
    private readonly bool _cartHasItems;
    private readonly BackupService _backupService;
    private bool _isBusy;
    private bool _allowClose;
    private bool _isLoading;
    private int _loadedBackupIntervalDays;
    private SettingsFormSnapshot _savedSnapshot = null!;

    public SettingsWindow()
        : this(new AppSettings(), false)
    {
    }

    public SettingsWindow(AppSettings current, bool cartHasItems, BackupService? backupService = null)
    {
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _cartHasItems = cartHasItems;
        _backupService = backupService ?? new BackupService();
        InitializeComponent();
        LoadSettings(current);
        Closing += OnClosing;
    }

    public async Task<AppSettings?> TryApplyAsync()
    {
        if (_isBusy)
        {
            return null;
        }

        if (!TryBuildSettings(out var settings, out var error))
        {
            ShowStatus(error);
            return null;
        }

        if (_cartHasItems && !PathsEqual(_current.DatabasePath, settings.DatabasePath))
        {
            CartDatabaseWarning.IsVisible = true;
            ShowStatus("장바구니에 상품이 있어 데이터베이스 설정을 변경할 수 없습니다.");
            return null;
        }

        SetBusy(true);
        try
        {
            var count = await ReadDatabaseAsync(settings.DatabasePath!);
            DatabaseStatusText.Text = $"연결 가능 · 상품 {count:N0}개";
            return settings;
        }
        catch (InvalidDataException)
        {
            ShowStatus("SimplePOS 형식이 아닙니다. 기존 형식 복사본 변환을 먼저 실행하세요.");
            return null;
        }
        catch (Exception exception)
        {
            ShowStatus($"데이터베이스를 확인하지 못했습니다. {exception.Message}");
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task<ExcelMigrationResult?> MigrateSelectedDatabaseAsync()
    {
        var source = DatabasePathInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            ShowStatus("변환할 Excel 데이터베이스를 선택하세요.");
            return null;
        }

        if (_cartHasItems)
        {
            CartDatabaseWarning.IsVisible = true;
            ShowStatus("장바구니에 상품이 있어 변환된 복사본으로 데이터베이스를 바꿀 수 없습니다.");
            return null;
        }

        SetBusy(true);
        try
        {
            var originalHash = await HashAsync(source);
            var result = await ExcelPosRepository.MigrateLegacyAsync(source);
            if (!originalHash.Equals(await HashAsync(source), StringComparison.Ordinal))
            {
                throw new IOException("변환 과정에서 원본 파일이 변경되었습니다.");
            }

            DatabasePathInput.Text = result.DestinationPath;
            DatabaseStatusText.Text =
                $"복사본 변환 완료 · 상품 {result.ProductCount:N0}개 · 원본 보존 · {Path.GetFileName(result.DestinationPath)}";
            ShowStatus(string.Empty);
            return result;
        }
        catch (Exception exception)
        {
            ShowStatus($"데이터베이스를 변환하지 못했습니다. {exception.Message}");
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task<BackupResult?> CreateBackupAsync()
    {
        if (!TryReadRetention(out var retention, out var error))
        {
            ShowStatus(error);
            return null;
        }

        var database = DatabasePathInput.Text?.Trim();
        var directory = BackupDirectoryInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(directory))
        {
            ShowStatus("데이터베이스와 백업 폴더를 모두 지정하세요.");
            return null;
        }

        SetBusy(true);
        try
        {
            var result = await _backupService.CreateAsync(database, directory, retention);
            BackupStatusText.Text = $"최근 성공 · {result.CreatedAt:yyyy.MM.dd HH:mm:ss} · {Path.GetFileName(result.Path)}";
            ShowStatus(string.Empty);
            return result;
        }
        catch (Exception exception)
        {
            BackupStatusText.Text = "최근 백업 실패";
            ShowStatus($"백업을 만들지 못했습니다. {exception.Message}");
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void LoadSettings(AppSettings settings)
    {
        _isLoading = true;
        DatabasePathInput.Text = settings.DatabasePath ?? string.Empty;
        BackupDirectoryInput.Text = settings.BackupDirectory
            ?? DefaultBackupDirectory(settings.DatabasePath);
        BackupIntervalInput.SelectedIndex = settings.BackupIntervalDays switch
        {
            3 => 1,
            7 => 2,
            30 => 3,
            _ => 0,
        };
        BackupTimeInput.Text = settings.BackupTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        _loadedBackupIntervalDays = settings.BackupIntervalDays;
        BackupRetentionInput.Value = settings.BackupRetentionCount;
        OutOfStockPolicyInput.SelectedIndex = settings.OutOfStockPolicy switch
        {
            OutOfStockPolicy.WarnAndAllow => 1,
            OutOfStockPolicy.Allow => 2,
            _ => 0,
        };
        ScannerSoundInput.IsChecked = settings.ScannerSuccessSoundEnabled;
        ScannerTerminatorInput.SelectedIndex =
            string.Equals(settings.ScannerTerminatorKey, "Tab", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        StartFullscreenInput.IsChecked = settings.StartFullScreen;
        RunAtLoginInput.IsChecked = settings.RunAtStartup;
        CartDatabaseWarning.IsVisible = _cartHasItems;
        _isLoading = false;
        _savedSnapshot = CaptureSnapshot();
    }

    private bool TryBuildSettings(out AppSettings settings, out string error)
    {
        settings = _current;
        error = string.Empty;
        var database = DatabasePathInput.Text?.Trim();
        var backupDirectory = BackupDirectoryInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            error = "상품 데이터베이스로 .xlsx 파일을 지정하세요.";
            return false;
        }
        if (!File.Exists(database))
        {
            error = "지정한 상품 데이터베이스 파일을 찾을 수 없습니다.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(backupDirectory))
        {
            error = "백업 폴더를 지정하세요.";
            return false;
        }
        string fullDatabase;
        string fullBackupDirectory;
        try
        {
            fullDatabase = Path.GetFullPath(database);
            fullBackupDirectory = Path.GetFullPath(backupDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"파일 또는 폴더 경로가 올바르지 않습니다. {exception.Message}";
            return false;
        }
        var databaseDirectory = Path.GetDirectoryName(fullDatabase);
        if (databaseDirectory is not null && PathsEqual(databaseDirectory, fullBackupDirectory))
        {
            error = "백업 폴더는 데이터베이스가 있는 폴더와 다르게 지정하세요.";
            return false;
        }
        if (!TimeOnly.TryParseExact(BackupTimeInput.Text?.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var backupTime))
        {
            error = "백업 실행 시각을 HH:mm 형식으로 입력하세요. 예: 03:00";
            return false;
        }
        if (!TryReadRetention(out var retention, out error))
        {
            return false;
        }

        settings = _current with
        {
            DatabasePath = fullDatabase,
            BackupDirectory = fullBackupDirectory,
            BackupIntervalDays = ReadBackupIntervalDays(),
            BackupTime = backupTime,
            BackupRetentionCount = retention,
            RunAtStartup = RunAtLoginInput.IsChecked == true,
            StartFullScreen = StartFullscreenInput.IsChecked == true,
            ScannerSuccessSoundEnabled = ScannerSoundInput.IsChecked == true,
            ScannerTerminatorKey = ScannerTerminatorInput.SelectedIndex == 1 ? "Tab" : "Enter",
            OutOfStockPolicy = OutOfStockPolicyInput.SelectedIndex switch
            {
                1 => OutOfStockPolicy.WarnAndAllow,
                2 => OutOfStockPolicy.Allow,
                _ => OutOfStockPolicy.Block,
            },
        };
        return true;
    }

    private bool TryReadRetention(out int retention, out string error)
    {
        retention = decimal.ToInt32(BackupRetentionInput.Value ?? 0);
        error = retention >= 1 ? string.Empty : "백업 보존 개수는 1 이상의 정수여야 합니다.";
        return retention >= 1;
    }

    private async Task TestDatabaseAsync()
    {
        var path = DatabasePathInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            ShowStatus("확인할 데이터베이스를 선택하세요.");
            return;
        }
        try
        {
            var count = await ReadDatabaseAsync(path);
            DatabaseStatusText.Text = $"연결 가능 · 상품 {count:N0}개";
            ShowStatus(string.Empty);
        }
        catch (Exception exception)
        {
            DatabaseStatusText.Text = "연결 실패";
            ShowStatus($"데이터베이스를 확인하지 못했습니다. {exception.Message}");
        }
    }

    private static async Task<int> ReadDatabaseAsync(string path) =>
        (await new ExcelPosRepository(path).GetProductsAsync()).Count;

    private async void OnSelectDatabaseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "상품 데이터베이스 선택",
                AllowMultiple = false,
                FileTypeFilter = [ExcelFileType],
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is not null)
            {
                DatabasePathInput.Text = path;
                await TestDatabaseAsync();
            }
        }
        catch (Exception exception)
        {
            ShowStatus($"파일 선택기를 열지 못했습니다. {exception.Message}");
        }
    }

    private async void OnSelectBackupFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "백업 폴더 선택",
                AllowMultiple = false,
            });
            var path = folders.FirstOrDefault()?.TryGetLocalPath();
            if (path is not null)
            {
                BackupDirectoryInput.Text = path;
            }
        }
        catch (Exception exception)
        {
            ShowStatus($"폴더 선택기를 열지 못했습니다. {exception.Message}");
        }
    }

    private async void OnTestDatabaseClick(object? sender, RoutedEventArgs e) => await TestDatabaseAsync();
    private async void OnMigrateDatabaseClick(object? sender, RoutedEventArgs e) => await MigrateSelectedDatabaseAsync();
    private async void OnBackupNowClick(object? sender, RoutedEventArgs e) => await CreateBackupAsync();

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var settings = await TryApplyAsync();
        if (settings is not null)
        {
            _allowClose = true;
            Close(settings);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => RequestClose();

    private void OnKeepEditingClick(object? sender, RoutedEventArgs e) => DiscardChangesPanel.IsVisible = false;

    private void OnDiscardChangesClick(object? sender, RoutedEventArgs e)
    {
        _allowClose = true;
        Close(null);
    }

    private void RequestClose()
    {
        if (HasUnsavedChanges())
        {
            DiscardChangesPanel.IsVisible = true;
            return;
        }
        _allowClose = true;
        Close(null);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }
        if (_isBusy)
        {
            e.Cancel = true;
            return;
        }
        if (HasUnsavedChanges())
        {
            e.Cancel = true;
            DiscardChangesPanel.IsVisible = true;
            return;
        }
        _allowClose = true;
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        SettingsContent.IsEnabled = !busy;
    }

    private void ShowStatus(string message)
    {
        SettingsStatusText.Text = message;
        SettingsStatusText.Foreground = new SolidColorBrush(Color.Parse("#B91C1C"));
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);
        }
        try
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string DefaultBackupDirectory(string? databasePath)
    {
        var parent = string.IsNullOrWhiteSpace(databasePath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : Path.GetDirectoryName(Path.GetFullPath(databasePath));
        return Path.Combine(parent ?? Environment.CurrentDirectory, "SimplePOS Backups");
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private int ReadBackupIntervalDays()
    {
        var selected = BackupIntervalInput.SelectedIndex switch { 1 => 3, 2 => 7, 3 => 30, _ => 1 };
        return _loadedBackupIntervalDays is not (1 or 3 or 7 or 30) && BackupIntervalInput.SelectedIndex == 0
            ? _loadedBackupIntervalDays
            : selected;
    }

    private bool HasUnsavedChanges() => !_isLoading && _savedSnapshot != CaptureSnapshot();

    private SettingsFormSnapshot CaptureSnapshot() => new(
        DatabasePathInput.Text ?? string.Empty,
        BackupDirectoryInput.Text ?? string.Empty,
        BackupIntervalInput.SelectedIndex,
        BackupTimeInput.Text ?? string.Empty,
        BackupRetentionInput.Value,
        OutOfStockPolicyInput.SelectedIndex,
        ScannerSoundInput.IsChecked,
        ScannerTerminatorInput.SelectedIndex,
        StartFullscreenInput.IsChecked,
        RunAtLoginInput.IsChecked);

    private sealed record SettingsFormSnapshot(
        string DatabasePath,
        string BackupDirectory,
        int BackupIntervalIndex,
        string BackupTime,
        decimal? BackupRetention,
        int OutOfStockPolicyIndex,
        bool? ScannerSound,
        int ScannerTerminatorIndex,
        bool? StartFullscreen,
        bool? RunAtLogin);
}
