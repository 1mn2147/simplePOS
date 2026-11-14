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
    private readonly Action _playScanSuccessSound;
    private object? _currentPage;
    private NavigationSection _currentSection = NavigationSection.Main;
    private BarcodeInputBuffer? _barcodeInput;
    private AppSettings _settings = new();
    private CartService _cart = new();
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
        IDialogService dialogs,
        Action? playScanSuccessSound = null)
    {
        _paths = paths;
        _settingsService = settingsService;
        _backupService = backupService;
        _startupService = startupService;
        _dialogs = dialogs;
        _playScanSuccessSound = playScanSuccessSound ?? (() => SystemSounds.Asterisk.Play());

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
            if (!ReferenceEquals(_currentPage, value) && _currentPage is VirtualInputViewModel input)
                input.DismissInputPanelCommand.Execute(null);
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
            var outcome = await _mainPage.HandleBarcodeAsync(result.Barcode);
            if (outcome == BarcodeHandlingResult.Added && _settings.ScannerSuccessSoundEnabled)
            {
                _playScanSuccessSound();
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
            case "main-undo":
                await NavigateMainAsync();
                _mainPage?.ClearCartCommand.Execute(null);
                break;
            case "products":
                await NavigateProductListAsync();
                break;
            case "products-selection":
                await NavigateProductListAsync();
                if (_productListPage is not null)
                {
                    _productListPage.SelectedProduct = _productListPage.Products.FirstOrDefault();
                    _productListPage.QuantityText = "3";
                }
                break;
            case "products-keyboard":
                await NavigateProductListAsync();
                _productListPage?.ShowKoreanKeyboardCommand.Execute("Search");
                _productListPage?.KoreanKeyCommand.Execute("ㄱ");
                _productListPage?.KoreanKeyCommand.Execute("ㅏ");
                break;
            case "editor":
                await OpenEditorAsync(null, "8801234567890", _mainPage);
                _productEditorPage?.ShowKoreanKeyboardCommand.Execute("ProductName");
                _productEditorPage?.KoreanKeyCommand.Execute("ㄱ");
                _productEditorPage?.KoreanKeyCommand.Execute("ㅏ");
                break;
            case "editor-form":
                await OpenEditorAsync(null, "8801234567890", _mainPage);
                break;
            case "editor-keypad":
                await OpenEditorAsync(null, "8801234567890", _mainPage);
                _productEditorPage?.ShowNumericKeypadCommand.Execute("Price");
                _productEditorPage?.NumericKeyCommand.Execute("1");
                _productEditorPage?.NumericKeyCommand.Execute("2");
                _productEditorPage?.NumericKeyCommand.Execute("3");
                _productEditorPage?.NumericKeyCommand.Execute("0");
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

        _mainPage = new MainPageViewModel(
            repository,
            _cart,
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
        _productListPage.AddToCartRequested = async (product, quantity) =>
        {
            if (!_mainPage.AddProduct(product, quantity)) return false;
            await NavigateMainAsync();
            return true;
        };
        _productEditorPage.Completed = FinishEditorAsync;
        _settingsPage.SettingsSaved = ApplySettingsAsync;
        _settingsPage.DatabaseReloadRequested = () => _mainPage.LoadAsync();
        _settingsPage.CanApplySettings = CanApplySettings;
        _settingsPage.Load(settings, _paths.DefaultDatabasePath, Path.Combine(_paths.RootDirectory, "backups"));
        _barcodeInput = new BarcodeInputBuffer(settings);
    }

    private async Task ApplySettingsAsync(AppSettings settings)
    {
        if (!SameDatabase(settings)) _cart = new CartService();
        _settings = settings;
        await BuildPagesAsync(settings);
        SettingsChanged?.Invoke(this, settings);
        await NavigateMainAsync();
        _mainPage?.ShowActivityFeedback("설정을 저장하고 적용했습니다.", "Success");
    }

    private async Task NavigateMainAsync()
    {
        if (!CanLeaveEditor()) return;
        if (_mainPage is null)
        {
            return;
        }

        CurrentPage = _mainPage;
        await _mainPage.LoadAsync();
    }

    private async Task NavigateProductListAsync()
    {
        if (!CanLeaveEditor()) return;
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
        if (!CanLeaveEditor()) return;
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
        var notice = _productEditorPage?.SavedProduct is not null
            ? _productEditorPage.SavedProductNotice
            : string.Empty;
        if (_editorReturnPage == _productListPage)
        {
            await NavigateProductListAsync();
            if (notice.Length > 0) _productListPage?.ShowNotice(notice);
        }
        else
        {
            await NavigateMainAsync();
            if (notice.Length > 0) _mainPage?.ShowActivityFeedback(notice, "Success");
        }
    }

    private void NavigateSettings()
    {
        if (!CanLeaveEditor()) return;
        if (_settingsPage is null)
        {
            return;
        }

        _settingsPage.Load(_settings, _paths.DefaultDatabasePath, Path.Combine(_paths.RootDirectory, "backups"));
        CurrentPage = _settingsPage;
    }

    private bool CanLeaveEditor()
    {
        if (_mainPage?.IsSavingSale == true)
        {
            _dialogs.Info("판매를 저장하고 있습니다. 저장이 끝난 뒤 이동하세요.", "판매 저장 중");
            return false;
        }
        return CurrentPage switch
        {
            ProductEditorViewModel editor => editor.ConfirmDiscardChanges(),
            SettingsViewModel settings => settings.ConfirmDiscardChanges(),
            _ => true,
        };
    }

    private bool SameDatabase(AppSettings settings) => string.Equals(
        Path.GetFullPath(settings.DatabasePath ?? _paths.DefaultDatabasePath),
        Path.GetFullPath(_settings.DatabasePath ?? _paths.DefaultDatabasePath),
        StringComparison.OrdinalIgnoreCase);

    private bool CanApplySettings(AppSettings settings)
    {
        if (_mainPage?.IsSavingSale == true) return false;
        if (SameDatabase(settings) || _cart.IsEmpty) return true;
        _dialogs.Info("장바구니에 상품이 있어 데이터베이스를 바꿀 수 없습니다. 판매를 완료하거나 장바구니를 비운 뒤 다시 선택하세요.", "장바구니 유지");
        return false;
    }
}
