using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using Microsoft.Win32;
using Pos.App.Services;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly string _settingsPath;
    private readonly JsonSettingsService _settingsService;
    private readonly BackupService _backupService;
    private readonly StartupRegistrationService _startupService;
    private readonly IDialogService _dialogs;
    private string _settingsStatusText = string.Empty;
    private string _databasePath = string.Empty;
    private string _databaseStatusText = string.Empty;
    private string _backupFolderPath = string.Empty;
    private string _selectedBackupFrequency = "매일";
    private string _backupTimeText = "03:00";
    private string _backupRetentionCountText = "30";
    private string _lastBackupStatusText = "백업 기록 없음";
    private string _selectedScannerTerminator = "Enter";
    private string _scannerInputThresholdText = "80";
    private bool _isScanSoundEnabled = true;
    private string _selectedTheme = "밝게";
    private bool _isStartFullscreen = true;
    private bool _isRunAtStartup;
    private string _selectedOutOfStockPolicy = "품절 시 차단";

    public SettingsViewModel(
        string settingsPath,
        JsonSettingsService settingsService,
        BackupService backupService,
        StartupRegistrationService startupService,
        IDialogService dialogs)
    {
        _settingsPath = settingsPath;
        _settingsService = settingsService;
        _backupService = backupService;
        _startupService = startupService;
        _dialogs = dialogs;

        SaveSettingsCommand = new AsyncRelayCommand(SaveAsync);
        SelectDatabaseCommand = new AsyncRelayCommand(SelectDatabaseAsync);
        TestDatabaseCommand = new AsyncRelayCommand(TestDatabaseAsync);
        ReloadDatabaseCommand = new AsyncRelayCommand(ReloadDatabaseAsync);
        SelectBackupFolderCommand = new RelayCommand(SelectBackupFolder);
        BackupNowCommand = new AsyncRelayCommand(BackupNowAsync);
        OpenBackupFolderCommand = new RelayCommand(OpenBackupFolder);
        ValidateBackupCommand = new AsyncRelayCommand(ValidateBackupAsync);
    }

    public IReadOnlyList<string> ScannerTerminatorOptions { get; } = ["Enter", "Tab"];
    public IReadOnlyList<string> BackupFrequencyOptions { get; } = ["매일", "3일마다", "7일마다", "30일마다"];
    public IReadOnlyList<string> ThemeOptions { get; } = ["밝게", "어둡게"];
    public IReadOnlyList<string> OutOfStockPolicyOptions { get; } = ["품절 시 차단", "경고 후 허용", "그냥 허용"];

    public Func<AppSettings, Task>? SettingsSaved { get; set; }

    public string SettingsStatusText { get => _settingsStatusText; private set => SetProperty(ref _settingsStatusText, value); }
    public string DatabasePath { get => _databasePath; private set => SetProperty(ref _databasePath, value); }
    public string DatabaseStatusText { get => _databaseStatusText; private set => SetProperty(ref _databaseStatusText, value); }
    public string BackupFolderPath { get => _backupFolderPath; private set => SetProperty(ref _backupFolderPath, value); }
    public string SelectedBackupFrequency { get => _selectedBackupFrequency; set => SetProperty(ref _selectedBackupFrequency, value); }
    public string BackupTimeText { get => _backupTimeText; set => SetProperty(ref _backupTimeText, value); }
    public string BackupRetentionCountText { get => _backupRetentionCountText; set => SetProperty(ref _backupRetentionCountText, value); }
    public string LastBackupStatusText { get => _lastBackupStatusText; private set => SetProperty(ref _lastBackupStatusText, value); }
    public string SelectedScannerTerminator { get => _selectedScannerTerminator; set => SetProperty(ref _selectedScannerTerminator, value); }
    public string ScannerInputThresholdText { get => _scannerInputThresholdText; set => SetProperty(ref _scannerInputThresholdText, value); }
    public bool IsScanSoundEnabled { get => _isScanSoundEnabled; set => SetProperty(ref _isScanSoundEnabled, value); }
    public string SelectedTheme { get => _selectedTheme; set => SetProperty(ref _selectedTheme, value); }
    public bool IsStartFullscreen { get => _isStartFullscreen; set => SetProperty(ref _isStartFullscreen, value); }
    public bool IsRunAtStartup { get => _isRunAtStartup; set => SetProperty(ref _isRunAtStartup, value); }
    public string SelectedOutOfStockPolicy { get => _selectedOutOfStockPolicy; set => SetProperty(ref _selectedOutOfStockPolicy, value); }

    public ICommand SaveSettingsCommand { get; }
    public ICommand SelectDatabaseCommand { get; }
    public ICommand TestDatabaseCommand { get; }
    public ICommand ReloadDatabaseCommand { get; }
    public ICommand SelectBackupFolderCommand { get; }
    public ICommand BackupNowCommand { get; }
    public ICommand OpenBackupFolderCommand { get; }
    public ICommand ValidateBackupCommand { get; }

    public void Load(AppSettings settings, string defaultDatabasePath, string defaultBackupPath)
    {
        DatabasePath = string.IsNullOrWhiteSpace(settings.DatabasePath) ? defaultDatabasePath : settings.DatabasePath;
        BackupFolderPath = string.IsNullOrWhiteSpace(settings.BackupDirectory) ? defaultBackupPath : settings.BackupDirectory;
        SelectedBackupFrequency = IntervalToLabel(settings.BackupIntervalDays);
        BackupTimeText = settings.BackupTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        BackupRetentionCountText = settings.BackupRetentionCount.ToString(CultureInfo.InvariantCulture);
        SelectedScannerTerminator = settings.ScannerTerminatorKey;
        ScannerInputThresholdText = settings.ScannerMaximumInterKeyDelayMilliseconds.ToString(CultureInfo.InvariantCulture);
        IsScanSoundEnabled = settings.ScannerSuccessSoundEnabled;
        SelectedTheme = settings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "어둡게" : "밝게";
        IsStartFullscreen = settings.StartFullScreen;
        IsRunAtStartup = settings.RunAtStartup;
        SelectedOutOfStockPolicy = PolicyToLabel(settings.OutOfStockPolicy);
        SettingsStatusText = "설정을 확인하세요.";
    }

    private async Task SaveAsync()
    {
        if (!TryBuildSettings(out var settings, out var error))
        {
            _dialogs.Error(error, "설정 확인");
            return;
        }

        try
        {
            SettingsStatusText = "저장 중";
            await EnsureDatabaseAsync(migrateLegacy: true);
            settings = settings with { DatabasePath = DatabasePath };
            await _settingsService.SaveAsync(_settingsPath, settings);
            _startupService.SetRegistered(settings.RunAtStartup, Environment.ProcessPath
                ?? throw new InvalidOperationException("실행 파일 경로를 확인할 수 없습니다."));
            SettingsStatusText = "저장됨";
            if (SettingsSaved is not null)
            {
                await SettingsSaved(settings);
            }
        }
        catch (Exception exception)
        {
            SettingsStatusText = "저장 실패";
            _dialogs.Error($"설정을 저장하지 못했습니다.\n\n{exception.Message}");
        }
    }

    private async Task SelectDatabaseAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "상품 데이터베이스 선택",
            Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        DatabasePath = dialog.FileName;
        await TestDatabaseAsync();
    }

    private async Task TestDatabaseAsync()
    {
        try
        {
            var count = await EnsureDatabaseAsync(migrateLegacy: false);
            DatabaseStatusText = $"연결 가능 · 상품 {count:N0}개";
        }
        catch (Exception exception)
        {
            DatabaseStatusText = "연결 실패";
            _dialogs.Error($"데이터베이스를 확인하지 못했습니다.\n\n{exception.Message}");
        }
    }

    private async Task ReloadDatabaseAsync()
    {
        if (!TryBuildSettings(out var settings, out var error))
        {
            _dialogs.Error(error, "설정 확인");
            return;
        }

        try
        {
            await EnsureDatabaseAsync(migrateLegacy: false);
            DatabaseStatusText = "다시 불러옴";
            if (SettingsSaved is not null)
            {
                await SettingsSaved(settings with { DatabasePath = DatabasePath });
            }
        }
        catch (Exception exception)
        {
            _dialogs.Error($"데이터베이스를 다시 불러오지 못했습니다.\n\n{exception.Message}");
        }
    }

    private void SelectBackupFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "백업 폴더 선택",
            Multiselect = false,
        };
        if (dialog.ShowDialog() == true)
        {
            BackupFolderPath = dialog.FolderName;
        }
    }

    private async Task BackupNowAsync()
    {
        if (!int.TryParse(BackupRetentionCountText, out var retention) || retention < 1)
        {
            _dialogs.Error("백업 보존 개수는 1 이상의 정수여야 합니다.", "백업 설정 확인");
            return;
        }

        try
        {
            LastBackupStatusText = "백업 중";
            var result = await _backupService.CreateAsync(DatabasePath, BackupFolderPath, retention);
            LastBackupStatusText = $"최근 성공 · {result.CreatedAt:yyyy.MM.dd HH:mm:ss} · {Path.GetFileName(result.Path)}";
        }
        catch (Exception exception)
        {
            LastBackupStatusText = "최근 백업 실패";
            _dialogs.Error($"백업을 만들지 못했습니다.\n\n{exception.Message}");
        }
    }

    private void OpenBackupFolder()
    {
        if (!Directory.Exists(BackupFolderPath))
        {
            _dialogs.Error("백업 폴더가 아직 존재하지 않습니다.");
            return;
        }

        Process.Start(new ProcessStartInfo(BackupFolderPath) { UseShellExecute = true });
    }

    private async Task ValidateBackupAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "검사할 백업 선택",
            InitialDirectory = Directory.Exists(BackupFolderPath) ? BackupFolderPath : null,
            Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var valid = await _backupService.ValidateAsync(dialog.FileName);
        LastBackupStatusText = valid
            ? $"검사 통과 · {Path.GetFileName(dialog.FileName)}"
            : $"검사 실패 · {Path.GetFileName(dialog.FileName)}";
        if (!valid)
        {
            _dialogs.Error("선택한 파일을 정상 Excel 백업으로 열 수 없습니다.", "백업 검사");
        }
    }

    private async Task<int> EnsureDatabaseAsync(bool migrateLegacy)
    {
        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            throw new InvalidOperationException("Excel 데이터베이스를 선택하세요.");
        }

        var repository = new ExcelPosRepository(DatabasePath);
        if (!File.Exists(DatabasePath))
        {
            await repository.CreateAsync();
            return 0;
        }

        try
        {
            return (await repository.GetProductsAsync()).Count;
        }
        catch (InvalidDataException) when (migrateLegacy)
        {
            if (!_dialogs.Confirm(
                    "기존 Excel 형식을 Simple POS 전용 복사본으로 변환할까요? 원본 파일은 변경하지 않습니다.",
                    "데이터베이스 변환"))
            {
                throw;
            }

            var migration = await ExcelPosRepository.MigrateLegacyAsync(DatabasePath);
            DatabasePath = migration.DestinationPath;
            DatabaseStatusText = $"변환 완료 · 상품 {migration.ProductCount:N0}개 · 빈 바코드 {migration.BlankBarcodeCount:N0}건";
            return migration.ProductCount;
        }
    }

    private bool TryBuildSettings(out AppSettings settings, out string error)
    {
        settings = new AppSettings();
        error = string.Empty;
        if (!TimeOnly.TryParseExact(BackupTimeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var backupTime))
        {
            error = "백업 실행 시각을 HH:mm 형식으로 입력하세요.";
            return false;
        }

        if (!int.TryParse(BackupRetentionCountText, out var retention) || retention < 1)
        {
            error = "백업 보존 개수는 1 이상의 정수여야 합니다.";
            return false;
        }

        if (!int.TryParse(ScannerInputThresholdText, out var threshold) || threshold < 1)
        {
            error = "스캐너 최대 입력 간격은 1 이상의 정수여야 합니다.";
            return false;
        }

        settings = new AppSettings
        {
            DatabasePath = DatabasePath,
            BackupDirectory = BackupFolderPath,
            BackupIntervalDays = LabelToInterval(SelectedBackupFrequency),
            BackupTime = backupTime,
            BackupRetentionCount = retention,
            RunAtStartup = IsRunAtStartup,
            StartFullScreen = IsStartFullscreen,
            Theme = SelectedTheme == "어둡게" ? "Dark" : "Light",
            ScannerSuccessSoundEnabled = IsScanSoundEnabled,
            ScannerTerminatorKey = SelectedScannerTerminator,
            ScannerMaximumInterKeyDelayMilliseconds = threshold,
            ScannerPartialInputTimeoutMilliseconds = Math.Max(500, threshold * 3),
            MinimumBarcodeLength = 3,
            OutOfStockPolicy = LabelToPolicy(SelectedOutOfStockPolicy),
        };
        return true;
    }

    private static int LabelToInterval(string label) => label switch
    {
        "3일마다" => 3,
        "7일마다" => 7,
        "30일마다" => 30,
        _ => 1,
    };

    private static string PolicyToLabel(OutOfStockPolicy policy) => policy switch
    {
        OutOfStockPolicy.WarnAndAllow => "경고 후 허용",
        OutOfStockPolicy.Allow => "그냥 허용",
        _ => "품절 시 차단",
    };

    private static OutOfStockPolicy LabelToPolicy(string label) => label switch
    {
        "경고 후 허용" => OutOfStockPolicy.WarnAndAllow,
        "그냥 허용" => OutOfStockPolicy.Allow,
        _ => OutOfStockPolicy.Block,
    };

    private static string IntervalToLabel(int days) => days switch
    {
        3 => "3일마다",
        7 => "7일마다",
        30 => "30일마다",
        _ => "매일",
    };
}
