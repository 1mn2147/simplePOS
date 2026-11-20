using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.Desktop.Tests;

public sealed class ProductManagementTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SimplePOS.ProductManagement.Tests",
        Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public async Task UnknownBarcodePrefillsValidatesSavesSearchesAndReturnsCartSelection()
    {
        var repository = await CreateRepositoryAsync();
        var owner = new Window();
        owner.Show();
        var window = new ProductManagementWindow(repository, "  NEW-88001  ");
        var dialog = window.ShowDialog<bool>(owner);

        try
        {
            await WaitForAsync(() => window.FindControl<TextBox>("ProductBarcodeInput")!.Text == "NEW-88001");
            Click(window.FindControl<Button>("SaveProductButton")!);
            await WaitForAsync(() => window.FindControl<TextBlock>("ProductValidationText")!.Text!.Contains("상품명"));
            Assert.Contains("error", window.FindControl<TextBox>("ProductNameInput")!.Classes);

            Set(window, "ProductNameInput", "  제주 보리차  ");
            Set(window, "ProductPriceInput", "1,500");
            Set(window, "ProductSupplierInput", " 제주상사 ");
            Set(window, "ProductStockInput", "12");
            Set(window, "ProductCategoryInput", " 음료 ");
            window.FindControl<NumericUpDown>("ProductSortOrderInput")!.Value = 4;
            window.FindControl<CheckBox>("ProductQuickAddInput")!.IsChecked = true;
            Click(window.FindControl<Button>("SaveProductButton")!);
            await WaitForAsync(async () => (await repository.GetProductsAsync()).Count == 1);

            var saved = Assert.Single(await repository.GetProductsAsync());
            Assert.Equal("제주 보리차", saved.Name);
            Assert.Equal(1_500, saved.Price);
            Assert.Equal("NEW-88001", saved.Barcode);
            Assert.Equal("제주상사", saved.Supplier);
            Assert.Equal(12, saved.Stock);
            Assert.Equal("음료", saved.Category);
            Assert.True(saved.QuickAdd);
            Assert.True(saved.IsActive);

            Set(window, "ProductSearchInput", "제주상사");
            await WaitForAsync(() => window.FindControl<ListBox>("ProductList")!.ItemCount == 1);
            window.FindControl<ListBox>("ProductList")!.SelectedItem = saved;
            Set(window, "ProductQuantityInput", "3");
            Click(window.FindControl<Button>("SelectProductButton")!);

            Assert.True(await dialog);
            Assert.Equal(saved.Id, window.SelectedProduct!.Id);
            Assert.Equal(3, window.SelectedQuantity);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close(false);
            }
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task EditPersistsEveryMutableProductField()
    {
        var repository = await CreateRepositoryAsync();
        var original = new Product
        {
            Name = "원본 상품",
            Price = 100,
            Barcode = "EDIT-1",
            Supplier = "기존 공급사",
            Stock = 2,
            Category = "기존",
            QuickAdd = false,
            IsActive = true,
            SortOrder = 1,
        };
        await repository.SaveProductAsync(original);
        var window = new ProductManagementWindow(repository);
        await window.InitializeAsync();
        window.Show();

        try
        {
            window.FindControl<ListBox>("ProductList")!.SelectedItem = Assert.Single(await repository.GetProductsAsync());
            Click(window.FindControl<Button>("EditProductButton")!);
            Set(window, "ProductNameInput", "수정 상품");
            Set(window, "ProductPriceInput", "2500");
            Set(window, "ProductBarcodeInput", "EDIT-2");
            Set(window, "ProductSupplierInput", "새 공급사");
            Set(window, "ProductStockInput", "");
            Set(window, "ProductCategoryInput", "수정 분류");
            window.FindControl<NumericUpDown>("ProductSortOrderInput")!.Value = 7;
            window.FindControl<CheckBox>("ProductQuickAddInput")!.IsChecked = true;
            window.FindControl<CheckBox>("ProductActiveInput")!.IsChecked = false;
            Click(window.FindControl<Button>("SaveProductButton")!);
            await WaitForAsync(async () => (await repository.GetProductsAsync()).Single().Name == "수정 상품");

            var saved = Assert.Single(await repository.GetProductsAsync());
            Assert.Equal(original.Id, saved.Id);
            Assert.Equal("수정 상품", saved.Name);
            Assert.Equal(2_500, saved.Price);
            Assert.Equal("EDIT-2", saved.Barcode);
            Assert.Equal("새 공급사", saved.Supplier);
            Assert.Null(saved.Stock);
            Assert.Equal("수정 분류", saved.Category);
            Assert.True(saved.QuickAdd);
            Assert.False(saved.IsActive);
            Assert.Equal(7, saved.SortOrder);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DuplicateBarcodeAndInvalidQuantityAreBlocked()
    {
        var repository = await CreateRepositoryAsync();
        await repository.SaveProductAsync(new Product { Name = "기존", Price = 1000, Barcode = "DUP-1" });
        var window = new ProductManagementWindow(repository);
        await window.InitializeAsync();
        window.Show();

        try
        {
            Click(window.FindControl<Button>("AddProductButton")!);
            Set(window, "ProductNameInput", "새 상품");
            Set(window, "ProductPriceInput", "2000");
            Set(window, "ProductBarcodeInput", "DUP-1");
            Click(window.FindControl<Button>("SaveProductButton")!);
            await WaitForAsync(() => window.FindControl<TextBlock>("ProductValidationText")!.Text!.Contains("이미 등록된"));
            Assert.Single(await repository.GetProductsAsync());

            window.FindControl<ListBox>("ProductList")!.SelectedItem = Assert.Single(await repository.GetProductsAsync());
            Set(window, "ProductQuantityInput", "0");
            Click(window.FindControl<Button>("SelectProductButton")!);
            Assert.Contains("수량은", window.FindControl<TextBlock>("ProductValidationText")!.Text);
            Assert.Null(window.SelectedProduct);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EditingLegacyDuplicateBarcodeWithoutChangingItIsAllowed()
    {
        var first = new Product { Name = "레거시 A", Price = 1000, Barcode = "LEGACY-DUP" };
        var second = new Product { Name = "레거시 B", Price = 2000, Barcode = "LEGACY-DUP" };
        var repository = new MemoryRepository([first, second]);
        var window = new ProductManagementWindow(repository);
        await window.InitializeAsync();
        window.Show();

        try
        {
            window.FindControl<ListBox>("ProductList")!.SelectedItem = first;
            Click(window.FindControl<Button>("EditProductButton")!);
            Set(window, "ProductSupplierInput", "수정 공급사");
            Click(window.FindControl<Button>("SaveProductButton")!);
            await WaitForAsync(() => repository.SavedProduct is not null);

            Assert.Equal(first.Id, repository.SavedProduct!.Id);
            Assert.Equal("LEGACY-DUP", repository.SavedProduct.Barcode);
            Assert.Equal("수정 공급사", repository.SavedProduct.Supplier);
            Assert.DoesNotContain("이미 등록된", window.FindControl<TextBlock>("ProductValidationText")!.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CommittedProductRemainsSavedWhenListRefreshFails()
    {
        var product = new Product { Name = "새로고침 상품", Price = 1000, Barcode = "REFRESH-1" };
        var repository = new RefreshFailingRepository(product);
        var window = new ProductManagementWindow(repository);
        await window.InitializeAsync();
        window.Show();

        try
        {
            window.FindControl<ListBox>("ProductList")!.SelectedItem = product;
            Click(window.FindControl<Button>("EditProductButton")!);
            Set(window, "ProductPriceInput", "1500");
            Click(window.FindControl<Button>("SaveProductButton")!);
            await WaitForAsync(() => repository.SavedProduct is not null);
            await WaitForAsync(() => window.FindControl<TextBlock>("ProductValidationText")!.Text!.Contains("저장되었습니다"));

            Assert.Equal(1500, repository.SavedProduct!.Price);
            Assert.Contains("새로고침", window.FindControl<TextBlock>("ProductValidationText")!.Text);
            Assert.False(window.FindControl<Button>("SaveProductButton")!.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ValidationWarningRequiresExplicitAcknowledgement()
    {
        var repository = await CreateRepositoryAsync();
        await repository.SaveProductAsync(new Product { Name = "같은 이름", Price = 1000 });
        var window = new ProductManagementWindow(repository);
        await window.InitializeAsync();
        window.Show();

        try
        {
            Click(window.FindControl<Button>("AddProductButton")!);
            Set(window, "ProductNameInput", "같은 이름");
            Set(window, "ProductPriceInput", "2000");
            Click(window.FindControl<Button>("SaveProductButton")!);
            await WaitForAsync(() => window.FindControl<CheckBox>("ProductWarningConfirmation")!.IsVisible);
            Assert.Single(await repository.GetProductsAsync());

            window.FindControl<CheckBox>("ProductWarningConfirmation")!.IsChecked = true;
            Click(window.FindControl<Button>("SaveProductButton")!);
            await WaitForAsync(async () => (await repository.GetProductsAsync()).Count == 2);
            Assert.Equal(2, (await repository.GetProductsAsync()).Count);
        }
        finally
        {
            window.Close();
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task<ExcelPosRepository> CreateRepositoryAsync()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, "상품 관리.xlsx");
        var repository = new ExcelPosRepository(path);
        await repository.CreateAsync();
        return repository;
    }

    private static void Set(ProductManagementWindow window, string name, string value)
    {
        window.FindControl<TextBox>(name)!.Text = value;
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The expected UI state was not reached.");
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 100 && !await condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(await condition(), "The expected persisted state was not reached.");
    }

    private class MemoryRepository(IEnumerable<Product> products) : IPosRepository
    {
        protected readonly List<Product> Products = [.. products];

        public Product? SavedProduct { get; protected set; }

        public virtual Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Product>>(Products.ToArray());

        public Task<Product?> FindProductByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult<Product?>(Products.FirstOrDefault(product => product.Barcode == barcode));

        public virtual Task SaveProductAsync(Product product, CancellationToken cancellationToken = default)
        {
            SavedProduct = product;
            var index = Products.FindIndex(existing => existing.Id == product.Id);
            if (index >= 0)
            {
                Products[index] = product;
            }
            else
            {
                Products.Add(product);
            }
            return Task.CompletedTask;
        }

        public Task SaveSaleAsync(
            Sale sale,
            IReadOnlyCollection<Product> inventoryUpdates,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RefreshFailingRepository(Product product) : MemoryRepository([product])
    {
        private bool _saved;

        public override Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) =>
            _saved
                ? Task.FromException<IReadOnlyList<Product>>(new IOException("새로고침 실패"))
                : base.GetProductsAsync(cancellationToken);

        public override async Task SaveProductAsync(Product product, CancellationToken cancellationToken = default)
        {
            await base.SaveProductAsync(product, cancellationToken);
            _saved = true;
        }
    }
}
