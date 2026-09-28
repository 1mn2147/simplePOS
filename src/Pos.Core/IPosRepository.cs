namespace Pos.Core;

public interface IPosRepository
{
    Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default);

    Task<Product?> FindProductByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);

    Task SaveProductAsync(Product product, CancellationToken cancellationToken = default);

    Task SaveSaleAsync(
        Sale sale,
        IReadOnlyCollection<Product> inventoryUpdates,
        CancellationToken cancellationToken = default);
}
