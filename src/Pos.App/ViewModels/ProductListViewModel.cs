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

    public ProductListViewModel(IPosRepository repository)
    {
        _repository = repository;
        SearchCommand = new RelayCommand(ApplyFilter);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        AddProductCommand = new AsyncRelayCommand(RequestAddAsync);
        EditSelectedProductCommand = new AsyncRelayCommand(RequestEditAsync, () => SelectedProduct is not null);
    }

    public ObservableCollection<Product> Products { get; } = [];
    public ObservableCollection<string> Categories { get; } = ["전체"];
    public IReadOnlyList<string> ActiveStatusOptions { get; } = ["사용 중", "전체", "사용 안 함"];

    public Func<Product?, Task>? EditRequested { get; set; }

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
            }
        }
    }

    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand AddProductCommand { get; }
    public ICommand EditSelectedProductCommand { get; }

    public async Task LoadAsync()
    {
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

    protected override string GetInputValue(string target) => target == "Search" ? SearchText : string.Empty;

    protected override void SetInputValue(string target, string value)
    {
        if (target == "Search")
        {
            SearchText = value;
        }
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
    }

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

    private Task RequestEditAsync() => SelectedProduct is null
        ? Task.CompletedTask
        : EditRequested?.Invoke(SelectedProduct) ?? Task.CompletedTask;
}
