using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Pos.App.Services;
using Pos.Core;

namespace Pos.App.ViewModels;

public enum BarcodeHandlingResult
{
    Added,
    UnknownBarcode,
    Blocked,
    Cancelled,
    Failed,
    Ignored,
}

public sealed class MainPageViewModel : ObservableObject, IDisposable
{
    private readonly IPosRepository _repository;
    private readonly CartService _cart;
    private readonly SaleService _saleService;
    private readonly IDialogService _dialogs;
    private readonly OutOfStockPolicy _outOfStockPolicy;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _saleNoticeTimer;
    private readonly DispatcherTimer _activityFeedbackTimer;
    private string _saleNoticeText = string.Empty;
    private string _activityFeedbackText = string.Empty;
    private string _activityFeedbackKind = "Information";
    private Guid? _recentProductId;
    private int _recentProductQuantity;
    private string _databaseStatusText = "연결 중";
    private string _backupStatusText = "대기";
    private string _currentTimeText = string.Empty;
    private int _quickAddColumns = 4;
    private bool _isSavingSale;
    public int QuickAddColumns { get => _quickAddColumns; set => SetProperty(ref _quickAddColumns, value); }
    public bool IsSavingSale { get => _isSavingSale; private set => SetProperty(ref _isSavingSale, value); }
    public string CompleteSaleText => IsSavingSale ? "판매 저장 중…" : "판매 완료";

    public MainPageViewModel(
        IPosRepository repository,
        CartService cart,
        SaleService saleService,
        IDialogService dialogs,
        OutOfStockPolicy outOfStockPolicy)
    {
        _repository = repository;
        _cart = cart;
        _saleService = saleService;
        _dialogs = dialogs;
        _outOfStockPolicy = outOfStockPolicy;

        IncreaseQuantityCommand = new RelayCommand<CartItemViewModel>(item => ChangeQuantity(item, 1));
        DecreaseQuantityCommand = new RelayCommand<CartItemViewModel>(item => ChangeQuantity(item, -1));
        RemoveCartItemCommand = new RelayCommand<CartItemViewModel>(RemoveItem, item => item is not null && !IsSavingSale);
        EditCartItemCommand = new AsyncRelayCommand<CartItemViewModel>(EditItemAsync);
        AddQuickItemCommand = new RelayCommand<QuickAddItemViewModel>(item => AddProduct(item?.Product));
        AddQuickProductCommand = new AsyncRelayCommand(
            () => AddQuickProductRequested?.Invoke() ?? Task.CompletedTask);
        ClearCartCommand = new RelayCommand(ClearCart, () => !_cart.IsEmpty && !IsSavingSale);
        UndoCartCommand = new RelayCommand(() => { _cart.UndoRemoval(); ClearActivityFeedback(); RefreshCart(); }, () => _cart.CanUndoRemoval && !IsSavingSale);
        CompleteSaleCommand = new AsyncRelayCommand(CompleteSaleAsync, () => !_cart.IsEmpty && !IsSavingSale);
        DismissSaleNoticeCommand = new RelayCommand(ClearSaleNotice);
        _saleNoticeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(8) };
        _saleNoticeTimer.Tick += (_, _) => ClearSaleNotice();
        DismissActivityFeedbackCommand = new RelayCommand(ClearActivityFeedback);
        _activityFeedbackTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(8) };
        _activityFeedbackTimer.Tick += (_, _) => ClearActivityFeedback();

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
        RefreshCart();
    }

    public string ApplicationName => "Simple POS";
    public ObservableCollection<CartItemViewModel> CartItems { get; } = [];
    public ObservableCollection<QuickAddItemViewModel> QuickAddItems { get; } = [];

    public Func<Product, Task>? EditProductRequested { get; set; }
    public Func<string, Task>? UnknownBarcodeRequested { get; set; }
    public Func<Task>? AddQuickProductRequested { get; set; }

    public string DatabaseStatusText
    {
        get => _databaseStatusText;
        private set
        {
            if (SetProperty(ref _databaseStatusText, value))
            {
                OnPropertyChanged(nameof(SalesStatusText));
                OnPropertyChanged(nameof(IsDatabaseReady));
            }
        }
    }
    public bool IsDatabaseReady => DatabaseStatusText.StartsWith("연결됨", StringComparison.Ordinal);
    public string SalesStatusText => IsDatabaseReady ? "판매 준비" : "DB 확인 필요";

    public string BackupStatusText
    {
        get => _backupStatusText;
        set => SetProperty(ref _backupStatusText, value);
    }

    public string CurrentTimeText
    {
        get => _currentTimeText;
        private set => SetProperty(ref _currentTimeText, value);
    }

    public int TotalItemCount => _cart.ItemCount;
    public long TotalAmount => _cart.Total;
    public bool IsCartEmpty => _cart.IsEmpty;
    public bool CanUndoCart => _cart.CanUndoRemoval;
    public string SaleNoticeText
    {
        get => _saleNoticeText;
        private set { if (SetProperty(ref _saleNoticeText, value)) OnPropertyChanged(nameof(HasSaleNotice)); }
    }
    public bool HasSaleNotice => SaleNoticeText.Length > 0;
    public string ActivityFeedbackText
    {
        get => _activityFeedbackText;
        private set { if (SetProperty(ref _activityFeedbackText, value)) OnPropertyChanged(nameof(HasActivityFeedback)); }
    }
    public bool HasActivityFeedback => ActivityFeedbackText.Length > 0;
    public string ActivityFeedbackKind { get => _activityFeedbackKind; private set => SetProperty(ref _activityFeedbackKind, value); }
    public Guid? RecentProductId { get => _recentProductId; private set => SetProperty(ref _recentProductId, value); }
    public int RecentProductQuantity { get => _recentProductQuantity; private set => SetProperty(ref _recentProductQuantity, value); }

    public ICommand IncreaseQuantityCommand { get; }
    public ICommand DecreaseQuantityCommand { get; }
    public RelayCommand<CartItemViewModel> RemoveCartItemCommand { get; }
    public ICommand EditCartItemCommand { get; }
    public ICommand AddQuickItemCommand { get; }
    public ICommand AddQuickProductCommand { get; }
    public RelayCommand ClearCartCommand { get; }
    public RelayCommand UndoCartCommand { get; }
    public AsyncRelayCommand CompleteSaleCommand { get; }
    public ICommand DismissSaleNoticeCommand { get; }
    public ICommand DismissActivityFeedbackCommand { get; }

    public async Task LoadAsync()
    {
        try
        {
            var products = await _repository.GetProductsAsync();
            QuickAddItems.Clear();
            foreach (var product in products
                         .Where(product => product.IsActive && product.QuickAdd)
                         .OrderBy(product => product.SortOrder)
                         .ThenBy(product => product.Name, StringComparer.CurrentCulture))
            {
                QuickAddItems.Add(new QuickAddItemViewModel(product, _outOfStockPolicy));
            }

            DatabaseStatusText = $"연결됨 · 상품 {products.Count:N0}개";
        }
        catch (Exception exception)
        {
            DatabaseStatusText = "연결 실패";
            _dialogs.Error($"상품 데이터베이스를 열 수 없습니다.\n\n{exception.Message}");
        }
    }

    public async Task<BarcodeHandlingResult> HandleBarcodeAsync(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BarcodeHandlingResult.Ignored;
        }
        if (IsSavingSale)
        {
            ShowActivityFeedback("판매 저장 중입니다. 저장이 끝난 뒤 다시 스캔하세요.", "Warning");
            return BarcodeHandlingResult.Blocked;
        }

        try
        {
            var product = await _repository.FindProductByBarcodeAsync(barcode.Trim());
            if (product is null)
            {
                var inactiveProduct = (await _repository.GetProductsAsync()).FirstOrDefault(item =>
                    !item.IsActive && string.Equals(ProductValidator.NormalizeBarcode(item.Barcode),
                        ProductValidator.NormalizeBarcode(barcode), StringComparison.Ordinal));
                if (inactiveProduct is not null) return AddProductWithFeedback(inactiveProduct);
                ShowActivityFeedback($"미등록 바코드 {barcode.Trim()} · 상품을 등록한 뒤 다시 스캔하세요.", "Information");
                if (UnknownBarcodeRequested is not null)
                {
                    await UnknownBarcodeRequested(barcode.Trim());
                }

                return BarcodeHandlingResult.UnknownBarcode;
            }

            return AddProductWithFeedback(product);
        }
        catch (Exception exception)
        {
            ShowActivityFeedback("스캔 처리 실패 · 장바구니를 확인한 뒤 다시 스캔하세요.", "Error");
            _dialogs.Error($"바코드를 처리하지 못했습니다.\n\n{exception.Message}");
            return BarcodeHandlingResult.Failed;
        }
    }

    public void Dispose()
    {
        _clockTimer.Stop();
        _saleNoticeTimer.Stop();
        _activityFeedbackTimer.Stop();
    }

    public bool AddProduct(Product? product, int quantity = 1) => AddProductWithFeedback(product, quantity) == BarcodeHandlingResult.Added;

    private BarcodeHandlingResult AddProductWithFeedback(Product? product, int quantity = 1)
    {
        if (product is null) return BarcodeHandlingResult.Ignored;
        if (IsSavingSale)
        {
            ShowActivityFeedback("판매 저장 중입니다. 저장이 끝난 뒤 상품을 담으세요.", "Warning");
            return BarcodeHandlingResult.Blocked;
        }
        if (!product.IsActive)
        {
            ShowActivityFeedback($"{product.Name} · 판매 중지 상품이라 담지 않았습니다.", "Warning");
            return BarcodeHandlingResult.Blocked;
        }
        if (quantity is < 1 or > CartService.MaximumQuantity)
        {
            ShowActivityFeedback($"수량은 1~{CartService.MaximumQuantity:N0}개로 입력하세요.", "Warning");
            return BarcodeHandlingResult.Blocked;
        }
        var currentQuantity = _cart.Lines.FirstOrDefault(line => line.ProductId == product.Id)?.Quantity ?? 0;
        if (currentQuantity + quantity > CartService.MaximumQuantity)
        {
            _dialogs.Info($"한 상품은 최대 {CartService.MaximumQuantity:N0}개까지 담을 수 있습니다.", "수량 확인");
            ShowActivityFeedback($"{product.Name} · 최대 수량 {CartService.MaximumQuantity:N0}개를 초과해 담지 않았습니다.", "Warning");
            return BarcodeHandlingResult.Blocked;
        }

        var result = _cart.Add(product, quantity, outOfStockPolicy: _outOfStockPolicy);
        if (result.Status == CartMutationStatus.OutOfStockConfirmationRequired)
        {
            var confirmed = _dialogs.Confirm(
                $"'{product.Name}'의 재고가 부족합니다. 그래도 장바구니에 추가할까요?",
                "재고 부족");
            if (confirmed)
            {
                result = _cart.Add(
                    product,
                    quantity,
                    outOfStockPolicy: _outOfStockPolicy,
                    outOfStockConfirmed: true);
            }
            else
            {
                ShowActivityFeedback($"{product.Name} · 담기를 취소했습니다.", "Information");
                return BarcodeHandlingResult.Cancelled;
            }
        }

        if (result.Status == CartMutationStatus.OutOfStockBlocked)
        {
            _dialogs.Info($"'{product.Name}'은(는) 품절되어 추가할 수 없습니다.", "품절 상품");
            ShowActivityFeedback($"{product.Name} · 재고가 부족해 담지 않았습니다.", "Warning");
            return BarcodeHandlingResult.Blocked;
        }

        if (result.Succeeded)
        {
            ClearSaleNotice();
            ShowActivityFeedback($"{product.Name} · {quantity:N0}개 추가 · 현재 {result.Line!.Quantity:N0}개", "Success", product.Id, result.Line.Quantity);
            RefreshCart();
            return BarcodeHandlingResult.Added;
        }
        ShowActivityFeedback($"{product.Name} · 담지 못했습니다. 상품 정보를 확인하세요.", "Error");
        return BarcodeHandlingResult.Failed;
    }

    public void ShowActivityFeedback(string message, string kind = "Information", Guid? productId = null, int quantity = 0)
    {
        ActivityFeedbackKind = kind;
        RecentProductId = productId;
        RecentProductQuantity = quantity;
        ActivityFeedbackText = message;
        foreach (var item in CartItems) item.IsRecentlyAdded = item.ProductId == productId;
        _activityFeedbackTimer.Stop();
        _activityFeedbackTimer.Start();
    }

    private void ChangeQuantity(CartItemViewModel? item, int delta)
    {
        if (IsSavingSale || item is null)
        {
            return;
        }

        CartMutationResult result;
        if (delta > 0)
        {
            result = _cart.Increment(item.ProductId, _outOfStockPolicy);
            if (result.Status == CartMutationStatus.OutOfStockConfirmationRequired &&
                _dialogs.Confirm("재고 수량을 초과합니다. 그래도 수량을 늘릴까요?", "재고 부족"))
            {
                result = _cart.Increment(item.ProductId, _outOfStockPolicy, outOfStockConfirmed: true);
            }
        }
        else
        {
            result = _cart.Decrement(item.ProductId);
        }

        if (result.Status == CartMutationStatus.OutOfStockBlocked)
        {
            _dialogs.Info("재고 수량을 초과해 추가할 수 없습니다.", "재고 부족");
        }

        if (result.Succeeded)
        {
            ClearActivityFeedback();
            RefreshCart();
        }
    }

    private void RemoveItem(CartItemViewModel? item)
    {
        if (!IsSavingSale && item is not null && _cart.Remove(item.ProductId))
        {
            ClearActivityFeedback();
            RefreshCart();
        }
    }

    private async Task EditItemAsync(CartItemViewModel? item)
    {
        if (item is not null && EditProductRequested is not null)
        {
            await EditProductRequested(item.Product);
        }
    }

    private void ClearCart()
    {
        if (IsSavingSale || _cart.IsEmpty)
        {
            return;
        }

        _cart.Clear();
        ClearActivityFeedback();
        RefreshCart();
    }

    private async Task CompleteSaleAsync()
    {
        if (_cart.IsEmpty || !_dialogs.Confirm(
                $"총 {TotalAmount.ToString("N0", CultureInfo.CurrentCulture)}원을 판매 완료할까요?",
                "판매 완료"))
        {
            return;
        }

        try
        {
            IsSavingSale = true;
            ClearActivityFeedback();
            OnPropertyChanged(nameof(CompleteSaleText));
            RefreshCart();
            var sale = await _saleService.CompleteSaleAsync(_cart);
            ClearActivityFeedback();
            SaleNoticeText = $"판매 완료 · {sale.Total:N0}원 · {sale.Lines.Sum(line => line.Quantity):N0}개";
            _saleNoticeTimer.Stop();
            _saleNoticeTimer.Start();
            RefreshCart();
            await LoadAsync();
        }
        catch (Exception exception)
        {
            _dialogs.Error($"판매를 저장하지 못했습니다. 장바구니는 유지됩니다.\n\n{exception.Message}");
            RefreshCart();
        }
        finally
        {
            IsSavingSale = false;
            OnPropertyChanged(nameof(CompleteSaleText));
            RefreshCart();
        }
    }

    private void RefreshCart()
    {
        CartItems.Clear();
        foreach (var line in _cart.Lines)
        {
            CartItems.Add(new CartItemViewModel(line) { IsRecentlyAdded = line.ProductId == RecentProductId });
        }

        OnPropertyChanged(nameof(TotalItemCount));
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(IsCartEmpty));
        OnPropertyChanged(nameof(CanUndoCart));
        UndoCartCommand.NotifyCanExecuteChanged();
        RemoveCartItemCommand.NotifyCanExecuteChanged();
        ClearCartCommand.NotifyCanExecuteChanged();
        CompleteSaleCommand.NotifyCanExecuteChanged();
    }

    private void UpdateClock()
    {
        CurrentTimeText = DateTime.Now.ToString("yyyy.MM.dd (ddd) HH:mm", CultureInfo.CurrentCulture);
    }

    private void ClearSaleNotice()
    {
        _saleNoticeTimer.Stop();
        SaleNoticeText = string.Empty;
    }

    private void ClearActivityFeedback()
    {
        _activityFeedbackTimer.Stop();
        ActivityFeedbackText = string.Empty;
        RecentProductId = null;
        RecentProductQuantity = 0;
        foreach (var item in CartItems) item.IsRecentlyAdded = false;
    }
}

public sealed class CartItemViewModel(CartLine line) : ObservableObject
{
    private bool _isRecentlyAdded;
    public bool IsRecentlyAdded { get => _isRecentlyAdded; set => SetProperty(ref _isRecentlyAdded, value); }
    public Product Product { get; } = line.Product;
    public Guid ProductId => line.ProductId;
    public string Name => line.ProductName;
    public long UnitPrice => line.UnitPrice;
    public int Quantity => line.Quantity;
    public long LineTotal => line.LineTotal;
}

public sealed class QuickAddItemViewModel(Product product, OutOfStockPolicy policy = OutOfStockPolicy.Block)
{
    public Product Product { get; } = product;
    public string Name => Product.Name;
    public long Price => Product.Price;
    public bool IsOutOfStock => Product.Stock == 0;
    public bool CanAdd => !IsOutOfStock || policy != OutOfStockPolicy.Block;
    public string StockStatusText => !IsOutOfStock ? string.Empty : policy switch
    {
        OutOfStockPolicy.Block => "품절",
        OutOfStockPolicy.WarnAndAllow => "재고 0 · 확인 후 판매",
        _ => "재고 0 · 판매 허용",
    };
}
