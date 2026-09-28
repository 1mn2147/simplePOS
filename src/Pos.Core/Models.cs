namespace Pos.Core;

public enum OutOfStockPolicy
{
    Block,
    WarnAndAllow,
    Allow,
}

public sealed record Product
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = string.Empty;

    public long Price { get; init; }

    public string? Barcode { get; init; }

    public string? Supplier { get; init; }

    public int? Stock { get; init; }

    public string? Category { get; init; }

    public bool QuickAdd { get; init; }

    public int SortOrder { get; init; }

    public bool IsActive { get; init; } = true;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CartLine
{
    internal CartLine(Product product, int quantity)
    {
        Product = product;
        Quantity = quantity;
    }

    public Product Product { get; private set; }

    public Guid ProductId => Product.Id;

    public string ProductName => Product.Name;

    public long UnitPrice => Product.Price;

    public int Quantity { get; internal set; }

    public long LineTotal => checked(UnitPrice * Quantity);

    internal void UpdateProduct(Product product)
    {
        Product = product;
    }
}

public sealed record SaleLine(
    Guid ProductId,
    string ProductName,
    long UnitPrice,
    int Quantity)
{
    public long LineTotal => checked(UnitPrice * Quantity);
}

public sealed record Sale(
    Guid Id,
    DateTimeOffset CompletedAt,
    IReadOnlyList<SaleLine> Lines,
    long Total);

public sealed record AppSettings
{
    public string? DatabasePath { get; init; }

    public string? BackupDirectory { get; init; }

    public int BackupIntervalDays { get; init; } = 1;

    public TimeOnly BackupTime { get; init; } = new(3, 0);

    public int BackupRetentionCount { get; init; } = 30;

    public bool RunAtStartup { get; init; }

    public bool StartFullScreen { get; init; } = true;

    public string Theme { get; init; } = "Light";

    public bool ScannerSuccessSoundEnabled { get; init; } = true;

    public string ScannerTerminatorKey { get; init; } = "Enter";

    public int ScannerMaximumInterKeyDelayMilliseconds { get; init; } = 80;

    public int ScannerPartialInputTimeoutMilliseconds { get; init; } = 500;

    public int MinimumBarcodeLength { get; init; } = 3;

    public OutOfStockPolicy OutOfStockPolicy { get; init; } = OutOfStockPolicy.Block;
}
