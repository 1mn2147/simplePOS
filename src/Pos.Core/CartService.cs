namespace Pos.Core;

public enum CartMutationStatus
{
    Added,
    QuantityChanged,
    Removed,
    OutOfStockBlocked,
    OutOfStockConfirmationRequired,
    NotFound,
}

public sealed record CartMutationResult(
    CartMutationStatus Status,
    CartLine? Line = null,
    string? Message = null)
{
    public bool Succeeded => Status is CartMutationStatus.Added
        or CartMutationStatus.QuantityChanged
        or CartMutationStatus.Removed;
}

public sealed class CartService
{
    public const int MaximumQuantity = 9_999;

    private readonly List<CartLine> _lines = [];

    public IReadOnlyList<CartLine> Lines => _lines.AsReadOnly();

    public int ItemCount => _lines.Sum(line => line.Quantity);

    public long Total => _lines.Aggregate(0L, (total, line) => checked(total + line.LineTotal));

    public bool IsEmpty => _lines.Count == 0;

    public CartMutationResult Add(
        Product product,
        int quantity = 1,
        OutOfStockPolicy outOfStockPolicy = OutOfStockPolicy.Block,
        bool outOfStockConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(product);
        ValidateQuantity(quantity);

        var existing = FindLine(product.Id);
        var targetQuantity = checked((existing?.Quantity ?? 0) + quantity);
        ValidateQuantity(targetQuantity);

        var stockResult = CheckStock(product, targetQuantity, outOfStockPolicy, outOfStockConfirmed);
        if (stockResult is not null)
        {
            return stockResult;
        }

        if (existing is null)
        {
            var line = new CartLine(product, targetQuantity);
            _lines.Add(line);
            return new CartMutationResult(CartMutationStatus.Added, line);
        }

        existing.UpdateProduct(product);
        existing.Quantity = targetQuantity;
        return new CartMutationResult(CartMutationStatus.QuantityChanged, existing);
    }

    public CartMutationResult SetQuantity(
        Guid productId,
        int quantity,
        OutOfStockPolicy outOfStockPolicy = OutOfStockPolicy.Block,
        bool outOfStockConfirmed = false)
    {
        var line = FindLine(productId);
        if (line is null)
        {
            return new CartMutationResult(CartMutationStatus.NotFound);
        }

        if (quantity <= 0)
        {
            _lines.Remove(line);
            return new CartMutationResult(CartMutationStatus.Removed, line);
        }

        ValidateQuantity(quantity);
        var stockResult = quantity > line.Quantity
            ? CheckStock(line.Product, quantity, outOfStockPolicy, outOfStockConfirmed)
            : null;
        if (stockResult is not null)
        {
            return stockResult;
        }

        line.Quantity = quantity;
        return new CartMutationResult(CartMutationStatus.QuantityChanged, line);
    }

    public CartMutationResult Increment(
        Guid productId,
        OutOfStockPolicy outOfStockPolicy = OutOfStockPolicy.Block,
        bool outOfStockConfirmed = false)
    {
        var line = FindLine(productId);
        return line is null
            ? new CartMutationResult(CartMutationStatus.NotFound)
            : SetQuantity(productId, checked(line.Quantity + 1), outOfStockPolicy, outOfStockConfirmed);
    }

    public CartMutationResult Decrement(Guid productId)
    {
        var line = FindLine(productId);
        return line is null
            ? new CartMutationResult(CartMutationStatus.NotFound)
            : SetQuantity(productId, line.Quantity - 1);
    }

    public bool Remove(Guid productId)
    {
        var line = FindLine(productId);
        return line is not null && _lines.Remove(line);
    }

    public void Clear()
    {
        _lines.Clear();
    }

    private CartLine? FindLine(Guid productId)
    {
        return _lines.FirstOrDefault(line => line.ProductId == productId);
    }

    private static CartMutationResult? CheckStock(
        Product product,
        int targetQuantity,
        OutOfStockPolicy policy,
        bool confirmed)
    {
        if (product.Stock is null || targetQuantity <= product.Stock.Value)
        {
            return null;
        }

        const string message = "요청 수량이 현재 재고보다 많습니다.";
        return policy switch
        {
            OutOfStockPolicy.Block => new CartMutationResult(
                CartMutationStatus.OutOfStockBlocked,
                Message: message),
            OutOfStockPolicy.WarnAndAllow when !confirmed => new CartMutationResult(
                CartMutationStatus.OutOfStockConfirmationRequired,
                Message: message),
            OutOfStockPolicy.WarnAndAllow => null,
            OutOfStockPolicy.Allow => null,
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "지원하지 않는 품절 정책입니다."),
        };
    }

    private static void ValidateQuantity(int quantity)
    {
        if (quantity is < 1 or > MaximumQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                $"수량은 1~{MaximumQuantity:N0} 범위여야 합니다.");
        }
    }
}
