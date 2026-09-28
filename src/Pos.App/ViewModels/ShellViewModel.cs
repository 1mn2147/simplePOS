using System.Media;
using System.Windows.Input;
using Pos.App.Services;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.App.ViewModels;

public enum NavigationSection
{
    Main,
    ProductList,
    ProductEditor,
    Settings,
}

public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly AppPaths _paths;
    private readonly JsonSettingsService _settingsService;
    private readonly BackupService _backupService;
    private readonly StartupRegistrationService _startupService;
    private readonly IDialogService _dialogs;
    private object? _currentPage;
    private NavigationSection _currentSection = NavigationSection.Main;
    private BarcodeInputBuffer? _barcodeInput;
    private AppSettings _settings = new();
    private MainPageViewModel? _mainPage;
    private ProductListViewModel? _productListPage;
    private ProductEditorViewModel? _productEditorPage;
    private SettingsViewModel? _settingsPage;
    private object? _editorReturnPage;

    public ShellViewModel(
        AppPaths paths,
        JsonSettingsService settingsService,
        BackupService backupService,
        StartupRegistrationService startupService,
        IDialogService dialogs)
    {
        _paths = paths;
        _settingsService = settingsService;
        _backupService = backupService;
        _startupService = startupService;
        _dialogs = dialogs;

        NavigateMainCommand = new AsyncRelayCommand(NavigateMainAsync);
        NavigateProductListCommand = new AsyncRelayCommand(NavigateProductListAsync);
        NavigateProductEditorCommand = new AsyncRelayCommand(() => OpenEditorAsync(null, null, _mainPage));
        NavigateSettingsCommand = new RelayCommand(NavigateSettings);
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public object? CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetProperty(ref _currentPage, value))
            {
                return;
            }

            CurrentSection = value switch
            {
                MainPageViewModel => NavigationSection.Main,
                ProductListViewModel => NavigationSection.ProductList,
                ProductEditorViewModel => NavigationSection.ProductEditor,
                SettingsViewModel => NavigationSection.Settings,
                _ => CurrentSection,
            };
        }
    }

    public NavigationSection CurrentSection
    {
        get => _currentSection;
        private set => SetProperty(ref _currentSection, value);
    }

    public AppSettings Settings => _settings;
    public MainPageViewModel? MainPage => _mainPage;

    public ICommand NavigateMainCommand { get; }
    public ICommand NavigateProductListCommand { get; }
    public ICommand NavigateProductEditorCommand { get; }
    public ICommand NavigateSettingsCommand { get; }

    public async Task InitializeAsync()
    {
        _settings = await _settingsService.LoadAsync(_paths.SettingsPath);
        var changed = false;
        if (string.IsNullOrWhiteSpace(_settings.DatabasePath))
        {
            _settings = _settings with { DatabasePath = _paths.DefaultDatabasePath };
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.BackupDirectory))
        {
            _settings = _settings with { BackupDirectory = Path.Combine(_paths.RootDirectory, "backups") };
            changed = true;
        }

        if (changed)
        {
            await _settingsService.SaveAsync(_paths.SettingsPath, _settings);
        }

        await BuildPagesAsync(_settings);
        await NavigateMainAsync();
    }

    public async Task<BarcodeInputStatus> HandleScannerKeyAsync(
        string key,
        DateTimeOffset occurredAt,
        bool textInputActive)
    {
        if (_mainPage is null || CurrentPage != _mainPage || _barcodeInput is null)
        {
            return BarcodeInputStatus.Ignored;
        }

        var result = _barcodeInput.PushKey(key, occurredAt, textInputActive);
        if (result.Status == BarcodeInputStatus.Completed && result.Barcode is not null)
        {
            await _mainPage.HandleBarcodeAsync(result.Barcode);
            if (_settings.ScannerSuccessSoundEnabled)
            {
                SystemSounds.Asterisk.Play();
            }
        }

        return result.Status;
    }

    public async Task PreparePreviewDataAsync()
    {
        var databasePath = _settings.DatabasePath
            ?? throw new InvalidOperationException("데이터베이스 경로가 설정되지 않았습니다.");
        var repository = new ExcelPosRepository(databasePath, _settings.OutOfStockPolicy);
        var products = await repository.GetProductsAsync();
        if (products.Count == 0)
        {
            var names = new[]
            {
                "아메리카노", "카페라떼", "생수 500ml", "콜라 캔",
                "감자칩", "초콜릿", "우유 1L", "계란 10구",
                "즉석밥", "컵라면", "물티슈", "종량제 봉투 20L",
            };
            for (var index = 0; index < names.Length; index++)
            {
                await repository.SaveProductAsync(new Product
                {
                    Name = names[index],
                    Price = 1_000 + (index * 500),
                    Barcode = $"880000000{index:0000}",
                    Stock = index == 9 ? 0 : 20,
                    Category = index < 4 ? "음료" : "생활/식품",
                    QuickAdd = true,
                    SortOrder = index,
                });
            }

            products = await repository.GetProductsAsync();
        }

        await NavigateMainAsync();
        if (_mainPage is not null)
        {
            foreach (var product in products.Where(product => product.Stock != 0).Take(3))
            {
                await _mainPage.HandleBarcodeAsync(product.Barcode ?? string.Empty);
            }
        }
    }

    public async Task SelectPreviewPageAsync(string page)
    {
        switch (page.ToLowerInvariant())
        {
            case "products":
                await NavigateProductListAsync();
                break;
            case "editor":
                await OpenEditorAsync(null, "8801234567890", _mainPage);
                _productEditorPage?.ShowKoreanKeyboardCommand.Execute("ProductName");
                break;
            case "quick-editor":
                await OpenEditorAsync(
                    null,
                    null,
                    _mainPage,
                    preselectQuickAdd: true);
                break;
            case "settings":
                NavigateSettings();
                break;
            default:
                await NavigateMainAsync();
                break;
        }
    }

    public void SetBackupStatus(string text)
    {
        if (_mainPage is not null)
        {
            _mainPage.BackupStatusText = text;
        }
    }

    public void Dispose() => _mainPage?.Dispose();

    private async Task BuildPagesAsync(AppSettings settings)
    {
        _mainPage?.Dispose();

        var databasePath = settings.DatabasePath
            ?? throw new InvalidOperationException("데이터베이스 경로가 설정되지 않았습니다.");
        var repository = new ExcelPosRepository(databasePath, settings.OutOfStockPolicy);
        if (!File.Exists(databasePath))
        {
            await repository.CreateAsync();
        }

        var cart = new CartService();
        _mainPage = new MainPageViewModel(
            repository,
            cart,
            new SaleService(repository),
            _dialogs,
            settings.OutOfStockPolicy);
        _productListPage = new ProductListViewModel(repository);
        _productEditorPage = new ProductEditorViewModel(repository, new ProductValidator(), _dialogs);
        _settingsPage = new SettingsViewModel(
            _paths.SettingsPath,
            _settingsService,
            _backupService,
            _startupService,
            _dialogs);

        _mainPage.EditProductRequested = product => OpenEditorAsync(product, null, _mainPage);
        _mainPage.UnknownBarcodeRequested = barcode => OpenEditorAsync(null, barcode, _mainPage);
        _mainPage.AddQuickProductRequested = () => OpenEditorAsync(
            null,
            null,
            _mainPage,
            preselectQuickAdd: true);
        _productListPage.EditRequested = product => OpenEditorAsync(product, null, _productListPage);
        _productEditorPage.Completed = FinishEditorAsync;
        _settingsPage.SettingsSaved = ApplySettingsAsync;
        _settingsPage.Load(settings, _paths.DefaultDatabasePath, Path.Combine(_paths.RootDirectory, "backups"));
        _barcodeInput = new BarcodeInputBuffer(settings);
    }

    private async Task ApplySettingsAsync(AppSettings settings)
    {
        _settings = settings;
        await BuildPagesAsync(settings);
        SettingsChanged?.Invoke(this, settings);
        await NavigateMainAsync();
    }

    private async Task NavigateMainAsync()
    {
        if (_mainPage is null)
        {
            return;
        }

        CurrentPage = _mainPage;
        await _mainPage.LoadAsync();
    }

    private async Task NavigateProductListAsync()
    {
        if (_productListPage is null)
        {
            return;
        }

        await _productListPage.LoadAsync();
        CurrentPage = _productListPage;
    }

    private async Task OpenEditorAsync(
        Product? product,
        string? barcode,
        object? returnPage,
        bool preselectQuickAdd = false)
    {
        if (_productEditorPage is null)
        {
            return;
        }

        _editorReturnPage = returnPage ?? _mainPage;
        await _productEditorPage.LoadAsync(product, barcode, preselectQuickAdd);
        CurrentPage = _productEditorPage;
    }

    private async Task FinishEditorAsync()
    {
        if (_editorReturnPage == _productListPage)
        {
            await NavigateProductListAsync();
        }
        else
        {
            await NavigateMainAsync();
        }
    }

    private void NavigateSettings()
    {
        if (_settingsPage is null)
        {
            return;
        }

        _settingsPage.Load(_settings, _paths.DefaultDatabasePath, Path.Combine(_paths.RootDirectory, "backups"));
        CurrentPage = _settingsPage;
    }
}
