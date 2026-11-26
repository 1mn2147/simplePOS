using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Pos.Core;

namespace Pos.Desktop;

public partial class ProductManagementWindow : Window
{
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#B91C1C"));
    private static readonly IBrush NormalBrush = new SolidColorBrush(Color.Parse("#102D55"));
    private readonly IPosRepository? _repository;
    private readonly ProductValidator _validator = new();
    private readonly List<Product> _allProducts = [];
    private readonly string? _newBarcode;
    private Product? _editingProduct;
    private bool _initialized;
    private bool _busy;
    private bool _reloadRequired;

    public ProductManagementWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        SetEditorEnabled(false);
        RefreshSelectionActions();
    }

    public ProductManagementWindow(IPosRepository repository, string? newBarcode = null) : this()
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _newBarcode = ProductValidator.NormalizeBarcode(newBarcode);
    }

    public Product? SelectedProduct { get; private set; }

    public int SelectedQuantity { get; private set; }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await ReloadProductsAsync();
        _initialized = true;
        if (_newBarcode is not null)
        {
            BeginEdit(null, _newBarcode);
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_repository is null)
        {
            return;
        }

        try
        {
            await InitializeAsync();
        }
        catch (Exception exception)
        {
            ShowValidation($"상품을 불러오지 못했습니다. {exception.Message}", true);
        }
    }

    private async Task ReloadProductsAsync(Guid? selectId = null)
    {
        var products = await EnsureRepository().GetProductsAsync();
        _allProducts.Clear();
        _allProducts.AddRange(products);
        _reloadRequired = false;
        ApplyFilter(selectId);
    }

    private void ApplyFilter(Guid? selectId = null)
    {
        var search = ProductValidator.NormalizeText(ProductSearchInput.Text);
        var filtered = _allProducts
            .Where(product => search.Length == 0 ||
                ProductValidator.NormalizeText(product.Name).Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                ProductValidator.NormalizeText(product.Supplier).Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                string.Equals(ProductValidator.NormalizeBarcode(product.Barcode), search, StringComparison.Ordinal))
            .OrderBy(product => product.SortOrder)
            .ThenBy(product => product.Name, StringComparer.CurrentCulture)
            .ToArray();
        ProductList.ItemsSource = filtered;
        ProductCountText.Text = $"검색 결과 {filtered.Length:N0}개 · 전체 {_allProducts.Count:N0}개";
        EmptyProductsText.IsVisible = filtered.Length == 0;
        ProductList.IsVisible = filtered.Length > 0;

        ProductList.SelectedItem = selectId.HasValue
            ? filtered.FirstOrDefault(product => product.Id == selectId.Value)
            : ProductList.SelectedItem is Product selected && filtered.Any(product => product.Id == selected.Id)
                ? filtered.First(product => product.Id == selected.Id)
                : null;
        RefreshSelectionActions();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private async void OnVirtualInputClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target })
        {
            return;
        }

        var (input, mode, label) = target switch
        {
            "Search" => (ProductSearchInput, VirtualInputMode.Korean, "상품 검색"),
            "Name" => (ProductNameInput, VirtualInputMode.Korean, "상품명"),
            "Price" => (ProductPriceInput, VirtualInputMode.Numeric, "가격"),
            "Supplier" => (ProductSupplierInput, VirtualInputMode.Korean, "공급사"),
            "Stock" => (ProductStockInput, VirtualInputMode.Numeric, "재고"),
            "Category" => (ProductCategoryInput, VirtualInputMode.Korean, "카테고리"),
            "Quantity" => (ProductQuantityInput, VirtualInputMode.Numeric, "수량"),
            _ => ((TextBox?)null, VirtualInputMode.Korean, string.Empty),
        };
        if (input is not null)
        {
            await VirtualInputDialog.EditAsync(this, input, mode, label);
        }
    }

    private void OnProductSelectionChanged(object? sender, SelectionChangedEventArgs e) => RefreshSelectionActions();

    private void RefreshSelectionActions()
    {
        var product = ProductList?.SelectedItem as Product;
        if (EditProductButton is null || SelectProductButton is null)
        {
            return;
        }

        EditProductButton.IsEnabled = product is not null && !_busy && !_reloadRequired;
        SelectProductButton.IsEnabled = product?.IsActive == true && !_busy && !_reloadRequired;
    }

    private void OnAddClick(object? sender, RoutedEventArgs e) => BeginEdit(null);

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (ProductList.SelectedItem is Product product)
        {
            BeginEdit(product);
        }
    }

    private void BeginEdit(Product? product, string? barcode = null)
    {
        _editingProduct = product;
        EditorTitle.Text = product is null ? "상품 추가" : "상품 수정";
        EditorHint.Text = barcode is null
            ? product is null ? "새 상품의 판매 정보를 입력하세요." : $"{product.Name}의 정보를 수정합니다."
            : "미등록 바코드입니다. 상품 정보를 저장하면 바로 장바구니에 담을 수 있습니다.";
        ProductNameInput.Text = product?.Name ?? string.Empty;
        ProductPriceInput.Text = product?.Price.ToString() ?? string.Empty;
        ProductBarcodeInput.Text = barcode ?? product?.Barcode ?? string.Empty;
        ProductSupplierInput.Text = product?.Supplier ?? string.Empty;
        ProductStockInput.Text = product?.Stock?.ToString() ?? string.Empty;
        ProductCategoryInput.Text = product?.Category ?? string.Empty;
        ProductSortOrderInput.Value = product?.SortOrder ?? Math.Max(0, _allProducts.Select(item => item.SortOrder).DefaultIfEmpty(-1).Max() + 1);
        ProductQuickAddInput.IsChecked = product?.QuickAdd ?? barcode is not null;
        ProductActiveInput.IsChecked = product?.IsActive ?? true;
        ProductWarningConfirmation.IsChecked = false;
        ProductWarningConfirmation.IsVisible = false;
        ClearValidationStyles();
        ShowValidation("상품 정보를 입력하고 저장하세요.");
        SetEditorEnabled(true);
        ProductNameInput.Focus();
    }

    private void OnCancelEditClick(object? sender, RoutedEventArgs e)
    {
        _editingProduct = null;
        ClearEditor();
        SetEditorEnabled(false);
        ShowValidation("상품을 선택해 수정하거나 새 상품을 추가하세요.");
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || _reloadRequired)
        {
            return;
        }

        ClearValidationStyles();
        var product = BuildProduct(out var parseIssues);
        var issues = parseIssues.ToList();
        var originalBarcode = ProductValidator.NormalizeBarcode(_editingProduct?.Barcode);
        var barcodeChanged = _editingProduct is null || !string.Equals(
            originalBarcode,
            ProductValidator.NormalizeBarcode(product.Barcode),
            StringComparison.Ordinal);
        issues.AddRange(_validator.Validate(product, barcodeChanged ? _allProducts : null).Issues);

        var errors = issues.Where(issue => issue.Severity == ProductValidationSeverity.Error).ToArray();
        var warnings = issues.Where(issue => issue.Severity == ProductValidationSeverity.Warning).ToArray();
        ApplyValidationStyles(issues);
        if (errors.Length > 0)
        {
            ProductWarningConfirmation.IsVisible = false;
            ShowValidation(string.Join(Environment.NewLine, errors.Select(issue => $"• {issue.Message}")), true);
            FocusIssue(errors[0].PropertyName);
            return;
        }

        if (warnings.Length > 0 && ProductWarningConfirmation.IsChecked != true)
        {
            ProductWarningConfirmation.IsVisible = true;
            ShowValidation(string.Join(Environment.NewLine, warnings.Select(issue => $"• {issue.Message}")), true);
            return;
        }

        try
        {
            SetBusy(true);
            var productToSave = product;
            await EnsureRepository().SaveProductAsync(productToSave);
            _editingProduct = productToSave;
            try
            {
                await ReloadProductsAsync(productToSave.Id);
                BeginEdit(ProductList.SelectedItem as Product ?? productToSave);
                ShowValidation($"{productToSave.Name} · 상품 정보가 저장되었습니다.");
            }
            catch (Exception exception)
            {
                ProductList.ItemsSource = Array.Empty<Product>();
                _reloadRequired = true;
                EmptyProductsText.IsVisible = true;
                EmptyProductsText.Text = "상품은 저장되었지만 목록을 새로고침하지 못했습니다. 창을 다시 열어 주세요.";
                ShowValidation(
                    $"{productToSave.Name} · 상품은 저장되었습니다. 목록을 새로고침하지 못했습니다. {exception.Message}",
                    true);
            }
        }
        catch (Exception exception)
        {
            ShowValidation($"상품을 저장하지 못했습니다. {exception.Message}", true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private Product BuildProduct(out IReadOnlyList<ProductValidationIssue> issues)
    {
        var found = new List<ProductValidationIssue>();
        var priceText = ProductValidator.NormalizeText(ProductPriceInput.Text).Replace(",", string.Empty, StringComparison.Ordinal);
        var stockText = ProductValidator.NormalizeText(ProductStockInput.Text).Replace(",", string.Empty, StringComparison.Ordinal);
        var validPrice = long.TryParse(priceText, out var price);
        var validStock = stockText.Length == 0 || int.TryParse(stockText, out _);
        if (!validPrice)
        {
            found.Add(new ProductValidationIssue(nameof(Product.Price), "가격은 숫자로 입력하세요.", ProductValidationSeverity.Error));
        }
        if (!validStock)
        {
            found.Add(new ProductValidationIssue(nameof(Product.Stock), "재고는 숫자로 입력하거나 비워 두세요.", ProductValidationSeverity.Error));
        }
        issues = found;

        return new Product
        {
            Id = _editingProduct?.Id ?? Guid.NewGuid(),
            Name = ProductValidator.NormalizeText(ProductNameInput.Text),
            Price = validPrice ? price : 0,
            Barcode = ProductValidator.NormalizeBarcode(ProductBarcodeInput.Text),
            Supplier = NullIfEmpty(ProductSupplierInput.Text),
            Stock = stockText.Length == 0 || !validStock ? null : int.Parse(stockText),
            Category = NullIfEmpty(ProductCategoryInput.Text),
            QuickAdd = ProductQuickAddInput.IsChecked == true,
            IsActive = ProductActiveInput.IsChecked == true,
            SortOrder = decimal.ToInt32(ProductSortOrderInput.Value ?? 0),
            UpdatedAt = _editingProduct?.UpdatedAt ?? DateTimeOffset.UtcNow,
        };
    }

    private void OnSelectProductClick(object? sender, RoutedEventArgs e)
    {
        if (ProductList.SelectedItem is not Product { IsActive: true } product)
        {
            return;
        }

        if (!int.TryParse(ProductValidator.NormalizeText(ProductQuantityInput.Text), out var quantity) ||
            quantity is < 1 or > CartService.MaximumQuantity)
        {
            ProductQuantityInput.Classes.Add("error");
            ShowValidation($"수량은 1~{CartService.MaximumQuantity:N0} 사이의 정수로 입력하세요.", true);
            ProductQuantityInput.Focus();
            return;
        }

        ProductQuantityInput.Classes.Remove("error");
        SelectedProduct = product;
        SelectedQuantity = quantity;
        Close(true);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (!_busy)
        {
            Close(false);
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
        }
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        SaveProductButton.Content = value ? "저장 중…" : "상품 저장";
        SaveProductButton.IsEnabled = !value && !_reloadRequired;
        ProductEditorPanel.IsEnabled = !value && !_reloadRequired;
        AddProductButton.IsEnabled = !value && !_reloadRequired;
        ProductSearchInput.IsEnabled = !value && !_reloadRequired;
        RefreshSelectionActions();
    }

    private void SetEditorEnabled(bool value) => ProductEditorPanel.IsEnabled = value;

    private void ClearEditor()
    {
        foreach (var input in TextInputs())
        {
            input.Clear();
        }
        ProductSortOrderInput.Value = 0;
        ProductQuickAddInput.IsChecked = false;
        ProductActiveInput.IsChecked = true;
        ProductWarningConfirmation.IsVisible = false;
    }

    private void ShowValidation(string message, bool isError = false)
    {
        ProductValidationText.Text = message;
        ProductValidationText.Foreground = isError ? ErrorBrush : NormalBrush;
    }

    private void ApplyValidationStyles(IEnumerable<ProductValidationIssue> issues)
    {
        foreach (var issue in issues.Where(issue => issue.Severity == ProductValidationSeverity.Error))
        {
            InputFor(issue.PropertyName)?.Classes.Add("error");
        }
    }

    private void ClearValidationStyles()
    {
        foreach (var input in TextInputs())
        {
            input.Classes.Remove("error");
        }
        ProductQuantityInput.Classes.Remove("error");
    }

    private void FocusIssue(string propertyName) => InputFor(propertyName)?.Focus();

    private TextBox? InputFor(string propertyName) => propertyName switch
    {
        nameof(Product.Name) => ProductNameInput,
        nameof(Product.Price) => ProductPriceInput,
        nameof(Product.Barcode) => ProductBarcodeInput,
        nameof(Product.Supplier) => ProductSupplierInput,
        nameof(Product.Stock) => ProductStockInput,
        nameof(Product.Category) => ProductCategoryInput,
        _ => null,
    };

    private IEnumerable<TextBox> TextInputs()
    {
        yield return ProductNameInput;
        yield return ProductPriceInput;
        yield return ProductBarcodeInput;
        yield return ProductSupplierInput;
        yield return ProductStockInput;
        yield return ProductCategoryInput;
    }

    private static string? NullIfEmpty(string? value)
    {
        var normalized = ProductValidator.NormalizeText(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private IPosRepository EnsureRepository() => _repository
        ?? throw new InvalidOperationException("상품 데이터 저장소가 연결되지 않았습니다.");
}
