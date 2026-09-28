namespace Pos.Core;

public sealed class SaleService
{
    private readonly IPosRepository _repository;
    private readonly TimeProvider _timeProvider;

    public SaleService(IPosRepository repository, TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Sale> CompleteSaleAsync(
        CartService cart,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cart);
        if (cart.IsEmpty)
        {
            throw new InvalidOperationException("빈 장바구니는 판매 완료할 수 없습니다.");
        }

        var completedAt = _timeProvider.GetUtcNow();
        var cartSnapshot = cart.Lines
            .Select(line => new SaleLine(
                line.ProductId,
                line.ProductName,
                line.UnitPrice,
                line.Quantity))
            .ToArray();
        var total = cartSnapshot.Aggregate(0L, (sum, line) => checked(sum + line.LineTotal));
        var sale = new Sale(Guid.NewGuid(), completedAt, cartSnapshot, total);

        var inventoryUpdates = cart.Lines
            .Where(line => line.Product.Stock.HasValue)
            .Select(line => line.Product with
            {
                Stock = Math.Max(0, line.Product.Stock!.Value - line.Quantity),
                UpdatedAt = completedAt,
            })
            .ToArray();

        await _repository.SaveSaleAsync(sale, inventoryUpdates, cancellationToken).ConfigureAwait(false);
        cart.Clear();
        return sale;
    }
}
