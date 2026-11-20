using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Pos.Core;

namespace Pos.Desktop;

public partial class SalesWindow : Window
{
    private static readonly IBrush RowBorderBrush = new SolidColorBrush(Color.Parse("#E8E8EC"));
    private readonly IPosRepository? _repository;
    private readonly OutOfStockPolicy _outOfStockPolicy;
    private readonly CartService _cart = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly TextBox _barcodeInput;
    private readonly TextBlock _databaseStatusText;
    private readonly TextBlock _statusMessage;
    private readonly TextBlock _emptyCartMessage;
    private readonly TextBlock _totalItemCountText;
    private readonly TextBlock _totalAmountText;
    private readonly StackPanel _cartItemsPanel;
    private readonly WrapPanel _quickAddPanel;
    private readonly Button _clearCartButton;
    private readonly Button _completeSaleButton;
    private readonly Border _saleConfirmationPanel;
    private readonly TextBlock _confirmationSummary;
    private bool _initialized;
    private bool _isBusy;
    private int _pendingScanCount;

    public SalesWindow()
    {
        InitializeComponent();
        _barcodeInput = this.FindControl<TextBox>("BarcodeInput")!;
        _databaseStatusText = this.FindControl<TextBlock>("DatabaseStatusText")!;
        _statusMessage = this.FindControl<TextBlock>("StatusMessage")!;
        _emptyCartMessage = this.FindControl<TextBlock>("EmptyCartMessage")!;
        _totalItemCountText = this.FindControl<TextBlock>("TotalItemCountText")!;
        _totalAmountText = this.FindControl<TextBlock>("TotalAmountText")!;
        _cartItemsPanel = this.FindControl<StackPanel>("CartItemsPanel")!;
        _quickAddPanel = this.FindControl<WrapPanel>("QuickAddPanel")!;
        _clearCartButton = this.FindControl<Button>("ClearCartButton")!;
        _completeSaleButton = this.FindControl<Button>("CompleteSaleButton")!;
        _saleConfirmationPanel = this.FindControl<Border>("SaleConfirmationPanel")!;
        _confirmationSummary = this.FindControl<TextBlock>("ConfirmationSummary")!;
        Opened += OnOpened;
    }

    public SalesWindow(IPosRepository repository, OutOfStockPolicy policy) : this()
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        _outOfStockPolicy = policy;
    }

    public async Task InitializeAsync()
    {
        EnsureRepository();
        if (_initialized)
        {
            FocusScanner();
            return;
        }

        await ReloadProductsAsync();
        _initialized = true;
        RefreshCart();
        FocusScanner();
    }

    private async Task ProcessBarcodeAsync(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            ShowStatus("바코드를 입력하세요.", isError: true);
            FocusScanner();
            return;
        }

        _pendingScanCount++;
        RefreshCart();
        await _scanGate.WaitAsync();
        try
        {
            if (IsMutationLocked)
            {
                ShowStatus("판매 확인 또는 저장 중입니다. 현재 작업을 마친 뒤 다시 스캔하세요.", isError: true);
                return;
            }

            var normalizedBarcode = barcode.Trim();
            var product = await EnsureRepository().FindProductByBarcodeAsync(normalizedBarcode);
            if (product is null)
            {
                ShowStatus(
                    $"미등록 바코드 {normalizedBarcode} · 현재 사용 중인 SimplePOS Excel DB에 상품을 먼저 등록해 주세요.",
                    isError: true);
                return;
            }

            await AddProductAsync(product);
        }
        catch (Exception exception)
        {
            ShowStatus($"바코드를 처리하지 못했습니다. {exception.Message}", isError: true);
        }
        finally
        {
            _scanGate.Release();
            _pendingScanCount--;
            RefreshCart();
            FocusScanner();
        }
    }

    private async Task<bool> CompleteSaleAsync()
    {
        if (_cart.IsEmpty || _isBusy)
        {
            return false;
        }

        SetBusy(true);
        var itemCount = _cart.ItemCount;
        var total = _cart.Total;
        try
        {
            await new SaleService(EnsureRepository()).CompleteSaleAsync(_cart);
            RefreshCart();
            ShowStatus($"판매 완료 · 상품 {itemCount:N0}개 · {total:N0}원");

            try
            {
                await ReloadProductsAsync();
            }
            catch (Exception exception)
            {
                _databaseStatusText.Text = "상품 목록 새로고침 필요";
                ShowCatalogUnavailable();
                ShowStatus(
                    $"판매 완료 · 판매는 저장되었습니다. 최신 상품 목록을 새로고침하지 못했습니다. 앱을 다시 열어 주세요. {exception.Message}",
                    isError: true);
            }

            return true;
        }
        catch (Exception exception)
        {
            RefreshCart();
            ShowStatus($"판매를 저장하지 못했습니다. 장바구니는 유지됩니다. {exception.Message}", isError: true);
            return false;
        }
        finally
        {
            SetBusy(false);
            FocusScanner();
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_repository is null)
        {
            _databaseStatusText.Text = "디자인 미리 보기";
            return;
        }

        try
        {
            await InitializeAsync();
        }
        catch (Exception exception)
        {
            _databaseStatusText.Text = "상품 데이터 연결 실패";
            ShowStatus($"상품 데이터베이스를 열 수 없습니다. {exception.Message}", isError: true);
        }
    }

    private async void OnBarcodeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        var barcode = _barcodeInput.Text ?? string.Empty;
        _barcodeInput.Clear();
        await ProcessBarcodeAsync(barcode);
    }

    private async void OnAddBarcodeClick(object? sender, RoutedEventArgs e)
    {
        var barcode = _barcodeInput.Text ?? string.Empty;
        _barcodeInput.Clear();
        await ProcessBarcodeAsync(barcode);
    }

    private void OnClearCartClick(object? sender, RoutedEventArgs e)
    {
        if (IsMutationLocked)
        {
            return;
        }

        _cart.Clear();
        RefreshCart();
        ShowStatus("장바구니를 비웠습니다.");
        FocusScanner();
    }

    private void OnCompleteSaleClick(object? sender, RoutedEventArgs e)
    {
        if (_cart.IsEmpty || IsCheckoutLocked)
        {
            return;
        }

        _confirmationSummary.Text = $"상품 {_cart.ItemCount:N0}개 · {_cart.Total:N0}원";
        _saleConfirmationPanel.IsVisible = true;
        RefreshCart();
        this.FindControl<Button>("ConfirmSaleButton")!.Focus();
    }

    private void OnCancelSaleClick(object? sender, RoutedEventArgs e)
    {
        _saleConfirmationPanel.IsVisible = false;
        RefreshCart();
        ShowStatus("판매가 취소되었습니다. 장바구니는 유지됩니다.");
        FocusScanner();
    }

    private async void OnConfirmSaleClick(object? sender, RoutedEventArgs e)
    {
        _saleConfirmationPanel.IsVisible = false;
        await CompleteSaleAsync();
    }

    private async Task ReloadProductsAsync()
    {
        var products = await EnsureRepository().GetProductsAsync();
        _quickAddPanel.Children.Clear();
        foreach (var product in products
                     .Where(product => product.IsActive && product.QuickAdd)
                     .OrderBy(product => product.SortOrder)
                     .ThenBy(product => product.Name, StringComparer.CurrentCulture))
        {
            var button = new Button
            {
                Content = product.Stock.HasValue
                    ? $"{product.Name}\n{product.Price:N0}원 · 재고 {product.Stock.Value:N0}개"
                    : $"{product.Name}\n{product.Price:N0}원",
                Tag = product,
                Margin = new Thickness(0, 0, 8, 8),
                MinWidth = 132,
                MinHeight = 68,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsEnabled = _outOfStockPolicy != OutOfStockPolicy.Block || product.Stock is not <= 0,
            };
            AutomationProperties.SetName(button, $"빠른 추가 {product.Name}");
            button.Click += OnQuickAddClick;
            _quickAddPanel.Children.Add(button);
        }

        if (_quickAddPanel.Children.Count == 0)
        {
            _quickAddPanel.Children.Add(new TextBlock
            {
                Text = products.Count == 0
                    ? "등록된 상품이 없습니다. 기존 SimplePOS Excel DB 경로를 실행 인수로 지정해 주세요."
                    : "빠른 추가 상품이 없습니다. 현재 macOS 판매 화면에서는 기존 Windows 앱에서 빠른 추가 상품을 설정한 DB를 사용합니다.",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 340,
            });
        }

        _databaseStatusText.Text = $"판매 준비 · 상품 {products.Count:N0}개";
    }

    private async void OnQuickAddClick(object? sender, RoutedEventArgs e)
    {
        if (IsMutationLocked)
        {
            return;
        }

        if (sender is Button { Tag: Product product })
        {
            await AddProductAsync(product);
        }

        FocusScanner();
    }

    private async Task AddProductAsync(Product product)
    {
        if (IsMutationLocked)
        {
            return;
        }

        if (!product.IsActive)
        {
            ShowStatus($"{product.Name} · 판매 중지 상품입니다.", isError: true);
            return;
        }

        CartMutationResult result;
        try
        {
            result = _cart.Add(product, outOfStockPolicy: _outOfStockPolicy);
            if (result.Status == CartMutationStatus.OutOfStockConfirmationRequired)
            {
                var confirmed = await ShowStockConfirmationAsync(product);
                if (confirmed)
                {
                    result = _cart.Add(product, outOfStockPolicy: _outOfStockPolicy, outOfStockConfirmed: true);
                }
            }
        }
        catch (ArgumentOutOfRangeException exception)
        {
            ShowStatus($"{product.Name} · {exception.Message}", isError: true);
            return;
        }

        if (!result.Succeeded)
        {
            ShowStatus($"{product.Name} · {result.Message ?? "장바구니에 담지 못했습니다."}", isError: true);
            return;
        }

        RefreshCart();
        ShowStatus($"{product.Name} · 수량 {result.Line!.Quantity:N0}개");
    }

    private async Task<bool> ShowStockConfirmationAsync(Product product)
    {
        var dialog = new SaleConfirmationDialog
        {
            Title = "재고 부족",
        };
        dialog.FindControl<TextBlock>("ConfirmationSummary")!.Text =
            $"{product.Name}의 재고가 부족합니다. 그래도 담을까요?";
        return await dialog.ShowDialog<bool>(this);
    }

    private void RefreshCart()
    {
        _cartItemsPanel.Children.Clear();
        foreach (var line in _cart.Lines)
        {
            _cartItemsPanel.Children.Add(CreateCartRow(line));
        }

        _emptyCartMessage.IsVisible = _cart.IsEmpty;
        _totalItemCountText.Text = $"{_cart.ItemCount:N0}개";
        _totalAmountText.Text = $"{_cart.Total:N0}원";
        _clearCartButton.IsEnabled = !_cart.IsEmpty && !IsMutationLocked;
        _completeSaleButton.IsEnabled = !_cart.IsEmpty && !IsCheckoutLocked;
    }

    private Control CreateCartRow(CartLine line)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
            MinHeight = 70,
        };
        var productText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        productText.Children.Add(new TextBlock { Text = line.ProductName, FontWeight = FontWeight.SemiBold, FontSize = 16 });
        productText.Children.Add(new TextBlock { Text = $"{line.UnitPrice:N0}원", Foreground = Brushes.DimGray });
        row.Children.Add(productText);

        var decrease = CreateCartButton("−", $"{line.ProductName} 수량 줄이기", 1, line, OnDecreaseClick);
        var quantity = new TextBlock
        {
            Text = line.Quantity.ToString("N0"),
            MinWidth = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontWeight = FontWeight.SemiBold,
        };
        Grid.SetColumn(quantity, 2);
        row.Children.Add(quantity);
        var increase = CreateCartButton("+", $"{line.ProductName} 수량 늘리기", 3, line, OnIncreaseClick);
        var remove = CreateCartButton("삭제", $"{line.ProductName} 삭제", 4, line, OnRemoveClick);
        remove.Foreground = new SolidColorBrush(Color.Parse("#B91C1C"));
        Grid.SetColumn(decrease, 1);
        Grid.SetColumn(increase, 3);
        Grid.SetColumn(remove, 4);
        row.Children.Add(decrease);
        row.Children.Add(increase);
        row.Children.Add(remove);
        return new Border
        {
            BorderBrush = RowBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row,
        };
    }

    private Button CreateCartButton(
        string text,
        string automationName,
        int column,
        CartLine line,
        EventHandler<RoutedEventArgs> handler)
    {
        var button = new Button
        {
            Content = text,
            Tag = line,
            Margin = new Thickness(4),
            MinWidth = 44,
            IsEnabled = !IsMutationLocked,
        };
        AutomationProperties.SetName(button, automationName);
        button.Click += handler;
        Grid.SetColumn(button, column);
        return button;
    }

    private void OnDecreaseClick(object? sender, RoutedEventArgs e)
    {
        if (IsMutationLocked)
        {
            return;
        }

        if (sender is Button { Tag: CartLine line })
        {
            _cart.Decrement(line.ProductId);
            RefreshCart();
            ShowStatus($"{line.ProductName} · 수량 변경");
        }
    }

    private async void OnIncreaseClick(object? sender, RoutedEventArgs e)
    {
        if (IsMutationLocked || sender is not Button { Tag: CartLine line })
        {
            return;
        }

        CartMutationResult result;
        try
        {
            result = _cart.Increment(line.ProductId, _outOfStockPolicy);
            if (result.Status == CartMutationStatus.OutOfStockConfirmationRequired &&
                await ShowStockConfirmationAsync(line.Product))
            {
                result = _cart.Increment(line.ProductId, _outOfStockPolicy, outOfStockConfirmed: true);
            }
        }
        catch (ArgumentOutOfRangeException exception)
        {
            ShowStatus($"{line.ProductName} · {exception.Message}", isError: true);
            FocusScanner();
            return;
        }

        RefreshCart();
        ShowStatus(result.Succeeded
            ? $"{line.ProductName} · 수량 {result.Line!.Quantity:N0}개"
            : $"{line.ProductName} · {result.Message ?? "수량을 늘리지 못했습니다."}",
            isError: !result.Succeeded);
        FocusScanner();
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (IsMutationLocked)
        {
            return;
        }

        if (sender is Button { Tag: CartLine line })
        {
            _cart.Remove(line.ProductId);
            RefreshCart();
            ShowStatus($"{line.ProductName}을 장바구니에서 삭제했습니다.");
        }

        FocusScanner();
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        _barcodeInput.IsEnabled = !value;
        _completeSaleButton.Content = value ? "판매 저장 중…" : "판매 완료";
        RefreshCart();
    }

    private void ShowStatus(string message, bool isError = false)
    {
        _statusMessage.Text = message;
        _statusMessage.Foreground = new SolidColorBrush(Color.Parse(isError ? "#B91C1C" : "#102D55"));
    }

    private void FocusScanner() => Dispatcher.UIThread.Post(() => _barcodeInput.Focus());

    private bool IsMutationLocked => _isBusy || _saleConfirmationPanel.IsVisible;

    private bool IsCheckoutLocked => IsMutationLocked || _pendingScanCount > 0;

    private void ShowCatalogUnavailable()
    {
        _quickAddPanel.Children.Clear();
        _quickAddPanel.Children.Add(new TextBlock
        {
            Text = "상품 목록을 새로고침하지 못했습니다. 오래된 상품으로 판매하지 않도록 앱을 다시 열어 주세요.",
            Foreground = new SolidColorBrush(Color.Parse("#B91C1C")),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340,
        });
    }

    private IPosRepository EnsureRepository() => _repository
        ?? throw new InvalidOperationException("판매 데이터 저장소가 연결되지 않았습니다.");
}
