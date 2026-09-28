using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Pos.App.Services;
using Pos.Core;

namespace Pos.App.ViewModels;

public sealed class MainPageViewModel : ObservableObject, IDisposable
{
    private readonly IPosRepository _repository;
    private readonly CartService _cart;
    private readonly SaleService _saleService;
    private readonly IDialogService _dialogs;
    private readonly OutOfStockPolicy _outOfStockPolicy;
    private readonly DispatcherTimer _clockTimer;
    private string _databaseStatusText = "연결 중";
    private string _backupStatusText = "대기";
    private string _currentTimeText = string.Empty;

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
        RemoveCartItemCommand = new RelayCommand<CartItemViewModel>(RemoveItem);
        EditCartItemCommand = new AsyncRelayCommand<CartItemViewModel>(EditItemAsync);
        AddQuickItemCommand = new RelayCommand<QuickAddItemViewModel>(item => AddProduct(item?.Product));
        AddQuickProductCommand = new AsyncRelayCommand(
            () => AddQuickProductRequested?.Invoke() ?? Task.CompletedTask);
        ClearCartCommand = new RelayCommand(ClearCart, () => !_cart.IsEmpty);
        CompleteSaleCommand = new AsyncRelayCommand(CompleteSaleAsync, () => !_cart.IsEmpty);

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
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
        private set => SetProperty(ref _databaseStatusText, value);
    }

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

    public ICommand IncreaseQuantityCommand { get; }
    public ICommand DecreaseQuantityCommand { get; }
    public ICommand RemoveCartItemCommand { get; }
    public ICommand EditCartItemCommand { get; }
    public ICommand AddQuickItemCommand { get; }
    public ICommand AddQuickProductCommand { get; }
    public RelayCommand ClearCartCommand { get; }
    public AsyncRelayCommand CompleteSaleCommand { get; }

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
                QuickAddItems.Add(new QuickAddItemViewModel(product));
            }

            DatabaseStatusText = $"연결됨 · 상품 {products.Count:N0}개";
        }
        catch (Exception exception)
        {
            DatabaseStatusText = "연결 실패";
            _dialogs.Error($"상품 데이터베이스를 열 수 없습니다.\n\n{exception.Message}");
        }
    }

    public async Task HandleBarcodeAsync(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return;
        }

        try
        {
            var product = await _repository.FindProductByBarcodeAsync(barcode.Trim());
            if (product is null)
            {
                if (UnknownBarcodeRequested is not null)
                {
                    await UnknownBarcodeRequested(barcode.Trim());
                }

                return;
            }

            AddProduct(product);
        }
        catch (Exception exception)
        {
            _dialogs.Error($"바코드를 처리하지 못했습니다.\n\n{exception.Message}");
        }
    }

    public void Dispose()
    {
        _clockTimer.Stop();
    }

    private void AddProduct(Product? product)
    {
        if (product is null)
        {
            return;
        }

        var result = _cart.Add(product, outOfStockPolicy: _outOfStockPolicy);
        if (result.Status == CartMutationStatus.OutOfStockConfirmationRequired)
        {
            var confirmed = _dialogs.Confirm(
                $"'{product.Name}'의 재고가 부족합니다. 그래도 장바구니에 추가할까요?",
                "재고 부족");
            if (confirmed)
            {
                result = _cart.Add(
                    product,
                    outOfStockPolicy: _outOfStockPolicy,
                    outOfStockConfirmed: true);
            }
        }

        if (result.Status == CartMutationStatus.OutOfStockBlocked)
        {
            _dialogs.Info($"'{product.Name}'은(는) 품절되어 추가할 수 없습니다.", "품절 상품");
            return;
        }

        if (result.Succeeded)
        {
            RefreshCart();
        }
    }

    private void ChangeQuantity(CartItemViewModel? item, int delta)
    {
        if (item is null)
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
            RefreshCart();
        }
    }

    private void RemoveItem(CartItemViewModel? item)
    {
        if (item is not null && _cart.Remove(item.ProductId))
        {
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
        if (_cart.IsEmpty)
        {
            return;
        }

        _cart.Clear();
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
            var sale = await _saleService.CompleteSaleAsync(_cart);
            RefreshCart();
            await LoadAsync();
            _dialogs.Info(
                $"판매가 저장되었습니다.\n총액: {sale.Total.ToString("N0", CultureInfo.CurrentCulture)}원",
                "판매 완료");
        }
        catch (Exception exception)
        {
            _dialogs.Error($"판매를 저장하지 못했습니다. 장바구니는 유지됩니다.\n\n{exception.Message}");
            RefreshCart();
        }
    }

    private void RefreshCart()
    {
        CartItems.Clear();
        foreach (var line in _cart.Lines)
        {
            CartItems.Add(new CartItemViewModel(line));
        }

        OnPropertyChanged(nameof(TotalItemCount));
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(IsCartEmpty));
        ClearCartCommand.NotifyCanExecuteChanged();
        CompleteSaleCommand.NotifyCanExecuteChanged();
    }

    private void UpdateClock()
    {
        CurrentTimeText = DateTime.Now.ToString("yyyy.MM.dd (ddd) HH:mm", CultureInfo.CurrentCulture);
    }
}

public sealed class CartItemViewModel(CartLine line)
{
    public Product Product { get; } = line.Product;
    public Guid ProductId => line.ProductId;
    public string Name => line.ProductName;
    public long UnitPrice => line.UnitPrice;
    public int Quantity => line.Quantity;
    public long LineTotal => line.LineTotal;
}

public sealed class QuickAddItemViewModel(Product product)
{
    public Product Product { get; } = product;
    public string Name => Product.Name;
    public long Price => Product.Price;
    public bool IsOutOfStock => Product.Stock == 0;
}
