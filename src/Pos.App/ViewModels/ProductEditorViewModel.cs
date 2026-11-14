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
    private EditorState? _loadedState;
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
    private bool _validateOnChange;
    private Guid _editingId;
    private bool _startedFromUnknownBarcode;

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
        PropertyChanged += (_, args) =>
        {
            if (_validateOnChange && args.PropertyName is nameof(ProductName) or nameof(PriceText) or nameof(Barcode) or nameof(Supplier) or nameof(StockText) or nameof(CategoryText))
                ValidateInput();
        };
    }

    public ObservableCollection<string> Categories { get; } = [];
    public Func<Task>? Completed { get; set; }
    public Product? SavedProduct { get; private set; }
    public string SavedProductNotice { get; private set; } = string.Empty;

    public string EditorTitle => _original is null ? "상품 추가" : "상품 수정";

    public string EditorStatusText
    {
        get => _editorStatusText;
        private set { if (SetProperty(ref _editorStatusText, value)) OnPropertyChanged(nameof(EditorFeedbackText)); }
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

    public string ProductNameError => GetError(nameof(ProductName));
    public string PriceError => GetError(nameof(PriceText));
    public string BarcodeError => GetError(nameof(Barcode));
    public string SupplierError => GetError(nameof(Supplier));
    public string StockError => GetError(nameof(StockText));
    public string CategoryError => GetError(nameof(CategoryText));
    public string ValidationSummary => HasErrors ? $"입력 오류 {ErrorCount}개를 확인하세요." : string.Empty;
    public bool HasValidationSummary => HasErrors;
    public string EditorFeedbackText => HasErrors ? ValidationSummary : EditorStatusText;
    public override string? FirstInvalidField => new[] { nameof(ProductName), nameof(PriceText), nameof(Barcode), nameof(Supplier), nameof(StockText), nameof(CategoryText) }
        .FirstOrDefault(inputName => GetError(inputName).Length > 0);

    public ICommand SaveProductCommand { get; }
    public ICommand CancelCommand { get; }

    public async Task LoadAsync(
        Product? product = null,
        string? barcode = null,
        bool preselectQuickAdd = false)
    {
        _validateOnChange = false;
        SavedProduct = null;
        SavedProductNotice = string.Empty;
        _startedFromUnknownBarcode = product is null && !string.IsNullOrWhiteSpace(barcode);
        _editingId = product?.Id ?? Guid.NewGuid();
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
        EditorStatusText = _startedFromUnknownBarcode
            ? "미등록 바코드입니다. 상품을 저장한 뒤 다시 스캔해 담으세요."
            : string.Empty;
        SetValidationErrors(new Dictionary<string, string>());
        DismissInputPanelCommand.Execute(null);
        _loadedState = CaptureState();
        OnPropertyChanged(nameof(EditorTitle));
    }

    protected override string GetInputValue(string target) => target switch
    {
        "ProductName" => ProductName,
        "Supplier" => Supplier,
        "Category" => CategoryText,
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
            case "Category": CategoryText = value; break;
            case "Price": PriceText = value; break;
            case "Stock": StockText = value; break;
        }
    }

    private async Task SaveAsync()
    {
        ConfirmInputCommand.Execute(null);
        _validateOnChange = true;
        var (product, validation) = ValidateInput();
        if (HasErrors)
        {
            FocusFirstError();
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
            _loadedState = CaptureState();
            SavedProduct = product;
            SavedProductNotice = _startedFromUnknownBarcode
                ? $"{product.Name} · 등록 완료. 다시 스캔해 장바구니에 담으세요."
                : _original is null
                    ? $"{product.Name} · 상품 등록이 완료되었습니다."
                    : $"{product.Name} · 상품 정보가 저장되었습니다.";
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

    private Task CancelAsync() => ConfirmDiscardChanges() ? Completed?.Invoke() ?? Task.CompletedTask : Task.CompletedTask;

    public bool HasUnsavedChanges => _loadedState is not null && _loadedState != CaptureState();

    public bool ConfirmDiscardChanges()
    {
        if (!HasUnsavedChanges) return true;
        if (!_dialogs.Confirm("저장하지 않은 상품 정보가 있습니다. 변경 내용을 버리고 이동할까요?", "상품 정보 확인")) return false;
        _loadedState = CaptureState();
        return true;
    }

    private EditorState CaptureState() => new(ProductName, PriceText, Barcode, Supplier, StockText, CategoryText, IsQuickAdd, IsActive);
    private sealed record EditorState(string Name, string Price, string Barcode, string Supplier, string Stock, string Category, bool QuickAdd, bool Active);

    private (Product Product, ProductValidationResult Validation) ValidateInput()
    {
        var priceParsed = long.TryParse(PriceText, out var price);
        var stockParsed = int.TryParse(StockText, out var stock);
        var product = new Product
        {
            Id = _editingId,
            Name = ProductValidator.NormalizeText(ProductName),
            Price = price,
            Barcode = ProductValidator.NormalizeBarcode(Barcode),
            Supplier = ProductValidator.NormalizeText(Supplier),
            Stock = StockText.Length == 0 || !stockParsed ? null : stock,
            Category = ProductValidator.NormalizeText(CategoryText),
            QuickAdd = IsQuickAdd,
            SortOrder = _original?.SortOrder ?? (_existingProducts.Count == 0 ? 0 : _existingProducts.Max(item => item.SortOrder) + 1),
            IsActive = IsActive,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var validation = _validator.Validate(product, _existingProducts);
        var errors = new Dictionary<string, string>();
        foreach (var issue in validation.Issues.Where(issue => issue.Severity == ProductValidationSeverity.Error))
        {
            var field = issue.PropertyName switch
            {
                nameof(Product.Name) => nameof(ProductName), nameof(Product.Price) => nameof(PriceText),
                nameof(Product.Stock) => nameof(StockText), nameof(Product.Category) => nameof(CategoryText), _ => issue.PropertyName,
            };
            errors[field] = issue.Message;
        }
        if (!priceParsed) errors[nameof(PriceText)] = $"가격은 0~{ProductValidator.MaximumPrice:N0}원 범위의 정수로 입력하세요.";
        if (StockText.Length > 0 && !stockParsed) errors[nameof(StockText)] = "재고는 비워 두거나 0 이상의 정수로 입력하세요.";
        SetValidationErrors(errors);
        return (product, validation);
    }

    protected override void OnValidationChanged()
    {
        base.OnValidationChanged();
        foreach (var property in new[] { nameof(ProductNameError), nameof(PriceError), nameof(BarcodeError), nameof(SupplierError), nameof(StockError), nameof(CategoryError), nameof(ValidationSummary), nameof(HasValidationSummary), nameof(EditorFeedbackText) })
            OnPropertyChanged(property);
    }
}
