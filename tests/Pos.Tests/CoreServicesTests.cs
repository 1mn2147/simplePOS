using Pos.Core;

namespace Pos.Tests;

public sealed class CoreServicesTests
{
    [Fact]
    public void Cart_BlocksOrRequiresConfirmation_WhenRequestedQuantityExceedsStock()
    {
        var product = CreateProduct("품절 상품", 1_500, stock: 0);
        var cart = new CartService();

        var blocked = cart.Add(product, outOfStockPolicy: OutOfStockPolicy.Block);
        Assert.Equal(CartMutationStatus.OutOfStockBlocked, blocked.Status);
        Assert.Empty(cart.Lines);

        var warning = cart.Add(product, outOfStockPolicy: OutOfStockPolicy.WarnAndAllow);
        Assert.Equal(CartMutationStatus.OutOfStockConfirmationRequired, warning.Status);
        Assert.Empty(cart.Lines);

        var allowed = cart.Add(
            product,
            quantity: 2,
            outOfStockPolicy: OutOfStockPolicy.WarnAndAllow,
            outOfStockConfirmed: true);
        Assert.Equal(CartMutationStatus.Added, allowed.Status);
        Assert.Single(cart.Lines);

        var reduced = cart.Decrement(product.Id);
        Assert.Equal(CartMutationStatus.QuantityChanged, reduced.Status);
        Assert.Equal(1, Assert.Single(cart.Lines).Quantity);
    }

    [Fact]
    public void Cart_AllowPolicyAddsAndIncrementsWithoutConfirmation()
    {
        var product = CreateProduct("품절 상품", 1_500, stock: 0);
        var cart = new CartService();

        var added = cart.Add(product, outOfStockPolicy: OutOfStockPolicy.Allow);
        var incremented = cart.Increment(product.Id, OutOfStockPolicy.Allow);

        Assert.Equal(CartMutationStatus.Added, added.Status);
        Assert.Equal(CartMutationStatus.QuantityChanged, incremented.Status);
        Assert.Equal(2, Assert.Single(cart.Lines).Quantity);
    }

    [Fact]
    public void Cart_TracksQuantitiesAndCheckedTotals()
    {
        var first = CreateProduct("첫 상품", 1_200, stock: 10);
        var second = CreateProduct("둘째 상품", 500);
        var cart = new CartService();

        cart.Add(first, 2);
        cart.Add(first);
        cart.Add(second, 4);

        Assert.Equal(7, cart.ItemCount);
        Assert.Equal(5_600, cart.Total);
        Assert.Equal(3, Assert.Single(cart.Lines, line => line.ProductId == first.Id).Quantity);

        var changed = cart.Decrement(first.Id);
        Assert.Equal(CartMutationStatus.QuantityChanged, changed.Status);
        Assert.Equal(4_400, cart.Total);

        cart.SetQuantity(second.Id, 0);
        Assert.Single(cart.Lines);

        cart.Clear();
        Assert.True(cart.IsEmpty);
        Assert.Empty(cart.Lines);
        Assert.Equal(0, cart.ItemCount);
        Assert.Equal(0, cart.Total);
    }

    [Fact]
    public async Task Sale_ClearsCartOnlyAfterRepositorySaveSucceeds()
    {
        var product = CreateProduct("재고 상품", 2_000, stock: 2);
        var cart = new CartService();
        cart.Add(product, 2);
        var repository = new RecordingRepository();
        var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var service = new SaleService(repository, new FixedTimeProvider(now));

        var sale = await service.CompleteSaleAsync(cart);

        Assert.True(cart.IsEmpty);
        Assert.Equal(now, sale.CompletedAt);
        Assert.Equal(4_000, sale.Total);
        Assert.Equal(2, Assert.Single(sale.Lines).Quantity);
        Assert.Equal(0, Assert.Single(repository.InventoryUpdates!).Stock);
        Assert.Same(sale, repository.SavedSale);
    }

    [Fact]
    public async Task Sale_PreservesCartWhenRepositorySaveFails()
    {
        var cart = new CartService();
        cart.Add(CreateProduct("상품", 1_000));
        var repository = new RecordingRepository { ThrowOnSave = true };
        var service = new SaleService(repository);

        await Assert.ThrowsAsync<IOException>(() => service.CompleteSaleAsync(cart));

        Assert.False(cart.IsEmpty);
        Assert.Equal(1_000, cart.Total);
    }

    [Fact]
    public void ProductValidator_ReportsErrorsAndNonBlockingWarnings()
    {
        var existing = CreateProduct(" 중복 이름 ", 1_000, barcode: "00123");
        var candidate = CreateProduct("중복 이름", 10_000_001, barcode: " 00123 ");
        var result = new ProductValidator().Validate(candidate, [existing]);

        Assert.False(result.IsValid);
        Assert.True(result.HasWarnings);
        Assert.Contains(result.Issues, issue =>
            issue.PropertyName == nameof(Product.Barcode)
            && issue.Severity == ProductValidationSeverity.Error);
        Assert.Contains(result.Issues, issue =>
            issue.PropertyName == nameof(Product.Name)
            && issue.Severity == ProductValidationSeverity.Warning);
        Assert.Contains(result.Issues, issue =>
            issue.PropertyName == nameof(Product.Price)
            && issue.Severity == ProductValidationSeverity.Warning);
    }

    [Fact]
    public void ProductValidator_RejectsInvalidCoreValues()
    {
        var product = CreateProduct(" ", -1, stock: -1) with
        {
            Id = Guid.Empty,
            SortOrder = -1,
        };

        var result = new ProductValidator().Validate(product);

        Assert.False(result.IsValid);
        Assert.Equal(5, result.Issues.Count(issue => issue.Severity == ProductValidationSeverity.Error));
    }

    private static Product CreateProduct(
        string name,
        long price,
        int? stock = null,
        string? barcode = null)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            Price = price,
            Stock = stock,
            Barcode = barcode,
            IsActive = true,
        };
    }

    private sealed class RecordingRepository : IPosRepository
    {
        public bool ThrowOnSave { get; init; }

        public Sale? SavedSale { get; private set; }

        public IReadOnlyCollection<Product>? InventoryUpdates { get; private set; }

        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Product>>([]);
        }

        public Task<Product?> FindProductByBarcodeAsync(
            string barcode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Product?>(null);
        }

        public Task SaveProductAsync(Product product, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SaveSaleAsync(
            Sale sale,
            IReadOnlyCollection<Product> inventoryUpdates,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new IOException("save failed");
            }

            SavedSale = sale;
            InventoryUpdates = inventoryUpdates;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
