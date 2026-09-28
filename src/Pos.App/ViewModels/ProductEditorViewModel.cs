using System.Collections.ObjectModel;
using System.Windows.Input;
using Pos.App.Services;
using Pos.Core;

namespace Pos.App.ViewModels;

public sealed class ProductEditorViewModel : VirtualInputViewModel
{
    private readonly IPosRepository _repository;
    private readonly ProductValidator _validator;
    private readonly IDialogService _dialogs;
    private readonly List<Product> _existingProducts = [];
    private Product? _original;
    private string _productName = string.Empty;
    private string _priceText = string.Empty;
    private string _barcode = string.Empty;
    private string _supplier = string.Empty;
    private string _stockText = string.Empty;
    private string _categoryText = string.Empty;
    private string? _selectedCategory;
    private bool _isQuickAdd;
    private bool _isActive = true;
    private string _editorStatusText = string.Empty;
    private string _productNameError = string.Empty;
    private string _priceError = string.Empty;
    private string _barcodeError = string.Empty;
    private string _supplierError = string.Empty;
    private string _stockError = string.Empty;
    private string _categoryError = string.Empty;
    private string _validationSummary = string.Empty;

    public ProductEditorViewModel(
        IPosRepository repository,
        ProductValidator validator,
        IDialogService dialogs)
    {
        _repository = repository;
        _validator = validator;
        _dialogs = dialogs;
        SaveProductCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new AsyncRelayCommand(CancelAsync);
    }

    public ObservableCollection<string> Categories { get; } = [];
    public Func<Task>? Completed { get; set; }

    public string EditorTitle => _original is null ? "상품 추가" : "상품 수정";

    public string EditorStatusText
    {
        get => _editorStatusText;
        private set => SetProperty(ref _editorStatusText, value);
    }

    public string ProductName { get => _productName; set => SetProperty(ref _productName, value); }
    public string PriceText { get => _priceText; set => SetProperty(ref _priceText, value); }
    public string Barcode { get => _barcode; set => SetProperty(ref _barcode, value); }
    public string Supplier { get => _supplier; set => SetProperty(ref _supplier, value); }
    public string StockText { get => _stockText; set => SetProperty(ref _stockText, value); }
    public string CategoryText { get => _categoryText; set => SetProperty(ref _categoryText, value); }

    public string? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value) && !string.IsNullOrWhiteSpace(value))
            {
                CategoryText = value;
            }
        }
    }

    public bool IsQuickAdd { get => _isQuickAdd; set => SetProperty(ref _isQuickAdd, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public string ProductNameError { get => _productNameError; private set => SetProperty(ref _productNameError, value); }
    public string PriceError { get => _priceError; private set => SetProperty(ref _priceError, value); }
    public string BarcodeError { get => _barcodeError; private set => SetProperty(ref _barcodeError, value); }
    public string SupplierError { get => _supplierError; private set => SetProperty(ref _supplierError, value); }
    public string StockError { get => _stockError; private set => SetProperty(ref _stockError, value); }
    public string CategoryError { get => _categoryError; private set => SetProperty(ref _categoryError, value); }
    public string ValidationSummary { get => _validationSummary; private set => SetProperty(ref _validationSummary, value); }
    public bool HasValidationSummary => ValidationSummary.Length > 0;

    public ICommand SaveProductCommand { get; }
    public ICommand CancelCommand { get; }

    public async Task LoadAsync(
        Product? product = null,
        string? barcode = null,
        bool preselectQuickAdd = false)
    {
        var products = await _repository.GetProductsAsync();
        _existingProducts.Clear();
        _existingProducts.AddRange(products);
        Categories.Clear();
        foreach (var category in products
                     .Select(item => ProductValidator.NormalizeText(item.Category))
                     .Where(item => item.Length > 0)
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(item => item, StringComparer.CurrentCulture))
        {
            Categories.Add(category);
        }

        _original = product;
        ProductName = product?.Name ?? string.Empty;
        PriceText = product?.Price.ToString() ?? string.Empty;
        Barcode = barcode ?? product?.Barcode ?? string.Empty;
        Supplier = product?.Supplier ?? string.Empty;
        StockText = product?.Stock?.ToString() ?? string.Empty;
        CategoryText = product?.Category ?? string.Empty;
        SelectedCategory = product?.Category;
        IsQuickAdd = product?.QuickAdd ?? preselectQuickAdd;
        IsActive = product?.IsActive ?? true;
        EditorStatusText = string.Empty;
        ClearErrors();
        OnPropertyChanged(nameof(EditorTitle));
    }

    protected override string GetInputValue(string target) => target switch
    {
        "ProductName" => ProductName,
        "Supplier" => Supplier,
        "Price" => PriceText,
        "Stock" => StockText,
        _ => string.Empty,
    };

    protected override void SetInputValue(string target, string value)
    {
        switch (target)
        {
            case "ProductName": ProductName = value; break;
            case "Supplier": Supplier = value; break;
            case "Price": PriceText = value; break;
            case "Stock": StockText = value; break;
        }
    }

    private async Task SaveAsync()
    {
        ClearErrors();
        if (!long.TryParse(PriceText, out var price))
        {
            PriceError = "가격은 0 이상의 정수로 입력하세요.";
        }

        int? stock = null;
        if (StockText.Length > 0)
        {
            if (int.TryParse(StockText, out var parsedStock))
            {
                stock = parsedStock;
            }
            else
            {
                StockError = "재고는 비워 두거나 0 이상의 정수로 입력하세요.";
            }
        }

        var product = new Product
        {
            Id = _original?.Id ?? Guid.NewGuid(),
            Name = ProductValidator.NormalizeText(ProductName),
            Price = price,
            Barcode = ProductValidator.NormalizeBarcode(Barcode),
            Supplier = ProductValidator.NormalizeText(Supplier),
            Stock = stock,
            Category = ProductValidator.NormalizeText(CategoryText),
            QuickAdd = IsQuickAdd,
            SortOrder = _original?.SortOrder ?? (_existingProducts.Count == 0 ? 0 : _existingProducts.Max(item => item.SortOrder) + 1),
            IsActive = IsActive,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var validation = _validator.Validate(product, _existingProducts);
        ApplyValidation(validation);
        if (!validation.IsValid || PriceError.Length > 0 || StockError.Length > 0)
        {
            ValidationSummary = "입력값을 확인하세요.";
            OnPropertyChanged(nameof(HasValidationSummary));
            return;
        }

        if (validation.HasWarnings && !_dialogs.Confirm(
                string.Join(Environment.NewLine, validation.Issues
                    .Where(issue => issue.Severity == ProductValidationSeverity.Warning)
                    .Select(issue => $"• {issue.Message}")) + Environment.NewLine + Environment.NewLine + "그래도 저장할까요?",
                "상품 정보 확인"))
        {
            return;
        }

        try
        {
            EditorStatusText = "저장 중";
            await _repository.SaveProductAsync(product);
            EditorStatusText = "저장됨";
            if (Completed is not null)
            {
                await Completed();
            }
        }
        catch (Exception exception)
        {
            EditorStatusText = "저장 실패";
            _dialogs.Error($"상품을 저장하지 못했습니다.\n\n{exception.Message}");
        }
    }

    private Task CancelAsync() => Completed?.Invoke() ?? Task.CompletedTask;

    private void ApplyValidation(ProductValidationResult validation)
    {
        foreach (var issue in validation.Issues.Where(issue => issue.Severity == ProductValidationSeverity.Error))
        {
            switch (issue.PropertyName)
            {
                case nameof(Product.Name): ProductNameError = issue.Message; break;
                case nameof(Product.Price): PriceError = issue.Message; break;
                case nameof(Product.Barcode): BarcodeError = issue.Message; break;
                case nameof(Product.Stock): StockError = issue.Message; break;
                case nameof(Product.Category): CategoryError = issue.Message; break;
            }
        }
    }

    private void ClearErrors()
    {
        ProductNameError = string.Empty;
        PriceError = string.Empty;
        BarcodeError = string.Empty;
        SupplierError = string.Empty;
        StockError = string.Empty;
        CategoryError = string.Empty;
        ValidationSummary = string.Empty;
        OnPropertyChanged(nameof(HasValidationSummary));
    }
}
