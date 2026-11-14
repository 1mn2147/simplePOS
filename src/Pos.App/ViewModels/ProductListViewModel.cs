using System.Collections.ObjectModel;
using System.Windows.Input;
using Pos.Core;

namespace Pos.App.ViewModels;

public sealed class ProductListViewModel : VirtualInputViewModel
{
    private readonly IPosRepository _repository;
    private readonly List<Product> _allProducts = [];
    private string _searchText = string.Empty;
    private string _selectedCategory = "전체";
    private string _selectedActiveStatus = "사용 중";
    private Product? _selectedProduct;
    private string _quantityText = "1";
    private string _selectionStatusText = "상품을 선택해 장바구니에 담으세요.";
    private string _noticeText = string.Empty;

    public ProductListViewModel(IPosRepository repository)
    {
        _repository = repository;
        SearchCommand = new RelayCommand(ApplyFilter);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        AddProductCommand = new AsyncRelayCommand(RequestAddAsync);
        EditSelectedProductCommand = new AsyncRelayCommand(RequestEditAsync, () => SelectedProduct is not null);
        AddToCartCommand = new AsyncRelayCommand(RequestAddToCartAsync, () => SelectedProduct?.IsActive == true);
        EmptyStateActionCommand = new AsyncRelayCommand(HandleEmptyStateAsync);
    }

    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<string> Categories { get; } = ["전체"];
    public IReadOnlyList<string> ActiveStatusOptions { get; } = ["사용 중", "전체", "사용 안 함"];

    public Func<Product?, Task>? EditRequested { get; set; }
    public Func<Product, int, Task<bool>>? AddToCartRequested { get; set; }
    public string QuantityText
    {
        get => _quantityText;
        set { if (SetProperty(ref _quantityText, value) && HasErrors) ValidateQuantity(); }
    }
    public string QuantityError => GetError(nameof(QuantityText));
    public string SelectionStatusText { get => _selectionStatusText; private set => SetProperty(ref _selectionStatusText, value); }
    public string SelectionTitle => SelectedProduct?.Name ?? "상품을 선택하세요.";
    public string SelectionHelpText => HasErrors ? QuantityError : SelectedProduct switch
    {
        null => "수량을 지정해 장바구니에 담을 수 있습니다.",
        { IsActive: false } => "판매 중지 · 상품 수정에서 판매를 허용하세요.",
        { Stock: 0 } => "재고 0 · 설정한 품절 정책이 적용됩니다.",
        { Stock: null } => "재고 미관리",
        _ => $"재고 {SelectedProduct.Stock:N0}개",
    };
    public string NoticeText { get => _noticeText; private set => SetProperty(ref _noticeText, value); }
    public void ShowNotice(string message) => NoticeText = message;

    public int ProductCount => Products.Count;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedActiveStatus
    {
        get => _selectedActiveStatus;
        set
        {
            if (SetProperty(ref _selectedActiveStatus, value))
            {
                ApplyFilter();
            }
        }
    }

    public Product? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (SetProperty(ref _selectedProduct, value))
            {
                ((AsyncRelayCommand)EditSelectedProductCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)AddToCartCommand).NotifyCanExecuteChanged();
                SelectionStatusText = DescribeSelection(value);
                OnPropertyChanged(nameof(SelectionTitle));
                OnPropertyChanged(nameof(SelectionHelpText));
            }
        }
    }

    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand AddProductCommand { get; }
    public ICommand EditSelectedProductCommand { get; }
    public ICommand AddToCartCommand { get; }
    public ICommand EmptyStateActionCommand { get; }

    public async Task LoadAsync()
    {
        NoticeText = string.Empty;
        var products = await _repository.GetProductsAsync();
        _allProducts.Clear();
        _allProducts.AddRange(products);

        Categories.Clear();
        Categories.Add("전체");
        foreach (var category in products
                     .Select(product => ProductValidator.NormalizeText(product.Category))
                     .Where(category => category.Length > 0)
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(category => category, StringComparer.CurrentCulture))
        {
            Categories.Add(category);
        }

        ApplyFilter();
    }

    protected override string GetInputValue(string target) => target switch { "Search" => SearchText, "Quantity" => QuantityText, _ => string.Empty };

    protected override void SetInputValue(string target, string value)
    {
        if (target == "Search")
        {
            SearchText = value;
        }
        else if (target == "Quantity") QuantityText = value;
    }

    private void ApplyFilter()
    {
        var search = ProductValidator.NormalizeText(SearchText);
        var filtered = _allProducts.Where(product =>
            MatchesStatus(product) &&
            (SelectedCategory == "전체" || string.Equals(
                ProductValidator.NormalizeText(product.Category),
                SelectedCategory,
                StringComparison.CurrentCultureIgnoreCase)) &&
            (search.Length == 0 ||
             ProductValidator.NormalizeText(product.Name).Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
             ProductValidator.NormalizeText(product.Supplier).Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
             string.Equals(ProductValidator.NormalizeBarcode(product.Barcode), search, StringComparison.Ordinal)));

        Products.Clear();
        foreach (var product in filtered.OrderBy(product => product.Name, StringComparer.CurrentCulture))
        {
            Products.Add(product);
        }

        OnPropertyChanged(nameof(ProductCount));
        if (SelectedProduct is not null && !Products.Contains(SelectedProduct)) SelectedProduct = null;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsCatalogEmpty));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
        OnPropertyChanged(nameof(EmptyStateActionText));
    }

    public bool IsEmpty => Products.Count == 0;
    public bool IsCatalogEmpty => _allProducts.Count == 0;
    private bool HasOnlyInactiveProducts => _allProducts.Count > 0 && _allProducts.All(product => !product.IsActive);
    public string EmptyStateTitle => IsCatalogEmpty ? "첫 상품을 등록하세요." : HasOnlyInactiveProducts && SelectedActiveStatus == "사용 중"
        ? "판매 중인 상품이 없습니다." : "검색 조건에 맞는 상품이 없습니다.";
    public string EmptyStateDescription => IsCatalogEmpty ? "상품을 직접 추가하거나 설정에서 기존 Excel 파일을 연결하세요."
        : HasOnlyInactiveProducts && SelectedActiveStatus == "사용 중" ? "전체 상품을 확인한 뒤 판매할 상품을 수정하세요."
        : "상품명·공급사를 짧게 입력하거나 검색 조건을 초기화하세요.";
    public string EmptyStateActionText => IsCatalogEmpty ? "첫 상품 추가" : HasOnlyInactiveProducts && SelectedActiveStatus == "사용 중"
        ? "전체 상품 보기" : "검색 조건 초기화";

    private Task HandleEmptyStateAsync()
    {
        if (IsCatalogEmpty) return RequestAddAsync();
        var showInactive = HasOnlyInactiveProducts;
        ClearSearch();
        if (showInactive) SelectedActiveStatus = "전체";
        return Task.CompletedTask;
    }

    private static string DescribeSelection(Product? product) => product switch
    {
        null => "상품을 선택해 장바구니에 담으세요.",
        { IsActive: false } => $"{product.Name} · 판매 중지 — 상품을 수정해 판매를 허용하세요.",
        { Stock: 0 } => $"{product.Name} · 재고 0 — 설정한 품절 정책이 적용됩니다.",
        { Stock: null } => $"{product.Name} · 재고 미관리",
        _ => $"{product.Name} · 재고 {product.Stock:N0}개",
    };

    private bool MatchesStatus(Product product) => SelectedActiveStatus switch
    {
        "사용 중" => product.IsActive,
        "사용 안 함" => !product.IsActive,
        _ => true,
    };

    private void ClearSearch()
    {
        SearchText = string.Empty;
        SelectedCategory = "전체";
        SelectedActiveStatus = "사용 중";
    }

    private Task RequestAddAsync() => EditRequested?.Invoke(null) ?? Task.CompletedTask;

    private async Task RequestAddToCartAsync()
    {
        if (SelectedProduct is not { IsActive: true } product) return;
        if (!ValidateQuantity())
        {
            FocusFirstError();
            return;
        }
        var quantity = int.Parse(QuantityText);
        ConfirmInputCommand.Execute(null);
        if (AddToCartRequested is not null && await AddToCartRequested(product, quantity)) QuantityText = "1";
    }

    private bool ValidateQuantity()
    {
        var valid = int.TryParse(QuantityText, out var quantity) && quantity is >= 1 and <= CartService.MaximumQuantity;
        SetValidationErrors(valid ? new Dictionary<string, string>() : new Dictionary<string, string>
        {
            [nameof(QuantityText)] = $"수량은 1~{CartService.MaximumQuantity:N0} 사이의 정수로 입력하세요.",
        });
        SelectionStatusText = valid ? DescribeSelection(SelectedProduct) : QuantityError;
        return valid;
    }

    protected override void OnValidationChanged()
    {
        base.OnValidationChanged();
        OnPropertyChanged(nameof(QuantityError));
        OnPropertyChanged(nameof(SelectionHelpText));
    }

    private Task RequestEditAsync() => SelectedProduct is null
        ? Task.CompletedTask
        : EditRequested?.Invoke(SelectedProduct) ?? Task.CompletedTask;
}
