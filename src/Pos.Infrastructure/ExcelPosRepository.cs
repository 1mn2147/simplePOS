using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using Pos.Core;

namespace Pos.Infrastructure;

public sealed record ExcelMigrationResult(
    string SourcePath,
    string DestinationPath,
    string LegacyWorksheet,
    int LegacyHeaderRow,
    int NameColumn,
    int PriceColumn,
    int BarcodeColumn,
    int ProductCount,
    int BlankBarcodeCount,
    int DuplicateBarcodeRowCount,
    int DuplicateBarcodeGroupCount,
    int DuplicateNameRowCount,
    int DuplicateNameGroupCount,
    int HighPriceCount);

public sealed class DuplicateBarcodeException : InvalidOperationException
{
    public DuplicateBarcodeException(string barcode, IReadOnlyList<Guid> productIds)
        : base($"Barcode '{barcode}' belongs to more than one active product.")
    {
        Barcode = barcode;
        ProductIds = productIds;
    }

    public string Barcode { get; }

    public IReadOnlyList<Guid> ProductIds { get; }
}

public sealed class ExcelDatabaseChangedException : IOException
{
    public ExcelDatabaseChangedException(string path)
        : base("The database workbook changed outside the application. Reload it before saving.")
    {
        Path = path;
    }

    public string Path { get; }
}

public sealed class ExcelPosRepository : IPosRepository
{
    public const int CurrentSchemaVersion = 1;

    private const string ProductsSheet = "Products";
    private const string CategoriesSheet = "Categories";
    private const string MetaSheet = "Meta";
    private const string SalesSheet = "Sales";
    private const string SaleItemsSheet = "SaleItems";
    private const string DatabaseKind = "SimplePOS";

    private static readonly string[] ProductHeaders =
    [
        "ProductId", "Name", "Price", "Barcode", "Supplier", "Stock", "Category",
        "QuickAdd", "SortOrder", "IsActive", "UpdatedAt",
    ];

    private static readonly string[] CategoryHeaders = ["Name", "SortOrder"];
    private static readonly string[] MetaHeaders = ["Key", "Value"];
    private static readonly string[] SaleHeaders = ["SaleId", "CompletedAt", "Total"];
    private static readonly string[] SaleItemHeaders =
    [
        "SaleId", "LineNumber", "ProductId", "ProductName", "UnitPrice", "Quantity", "LineTotal",
    ];

    private static readonly HashSet<string> ManagedSheetNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ProductsSheet,
        CategoriesSheet,
        MetaSheet,
        SalesSheet,
        SaleItemsSheet,
    };

    private static readonly HashSet<string> LegacyNameHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "제품이름", "제품명", "상품명", "상품이름", "이름", "name", "productname",
    };

    private static readonly HashSet<string> LegacyPriceHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "가격", "가격원", "판매가", "단가", "price", "unitprice",
    };

    private static readonly HashSet<string> LegacyBarcodeHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "바코드", "바코드라벨", "상품바코드", "제품바코드", "barcode", "ean", "ean13",
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private readonly ProductValidator _productValidator = new();
    private readonly OutOfStockPolicy _outOfStockPolicy;

    public ExcelPosRepository(
        string databasePath,
        OutOfStockPolicy outOfStockPolicy = OutOfStockPolicy.Block,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        if (!string.Equals(Path.GetExtension(DatabasePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The database path must use the .xlsx extension.", nameof(databasePath));
        }

        if (!Enum.IsDefined(outOfStockPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(outOfStockPolicy));
        }

        _outOfStockPolicy = outOfStockPolicy;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string DatabasePath { get; }

    public async Task CreateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(DatabasePath))
            {
                throw new IOException("The database workbook already exists.");
            }

            var directory = GetParentDirectory(DatabasePath);
            Directory.CreateDirectory(directory);
            var temporaryPath = CreateTemporaryWorkbookPath(DatabasePath);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var workbook = new XLWorkbook())
                {
                    var now = _timeProvider.GetUtcNow();
                    InitializeSchema(workbook, now);
                    workbook.SaveAs(temporaryPath);
                }

                ValidateDatabaseWorkbook(temporaryPath, expectedProductCount: 0, preservation: null);
                AtomicFileCommit.MoveNew(temporaryPath, DatabasePath);
            }
            catch
            {
                AtomicFileCommit.TryDelete(temporaryPath);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public static async Task<ExcelMigrationResult> MigrateLegacyAsync(
        string sourcePath,
        string? destinationPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("The legacy workbook does not exist.", fullSourcePath);
        }

        if (!string.Equals(Path.GetExtension(fullSourcePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only .xlsx workbooks can be migrated.");
        }

        var fullDestinationPath = destinationPath is null
            ? Path.Combine(
                GetParentDirectory(fullSourcePath),
                $"{Path.GetFileNameWithoutExtension(fullSourcePath)}_pos.xlsx")
            : Path.GetFullPath(destinationPath);

        if (!string.Equals(Path.GetExtension(fullDestinationPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The migration destination must use the .xlsx extension.", nameof(destinationPath));
        }

        if (PathsEqual(fullSourcePath, fullDestinationPath))
        {
            throw new InvalidOperationException("Migration must not overwrite the legacy workbook.");
        }

        var repository = new ExcelPosRepository(fullDestinationPath);
        return await repository.MigrateLegacyCoreAsync(fullSourcePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDatabaseExists();
            cancellationToken.ThrowIfCancellationRequested();
            using var workbook = new XLWorkbook(DatabasePath);
            ValidateSchema(workbook);
            var products = ReadProducts(workbook.Worksheet(ProductsSheet));
            return products
                .OrderBy(product => product.SortOrder)
                .ThenBy(product => product.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Product?> FindProductByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken = default)
    {
        var normalizedBarcode = ProductValidator.NormalizeBarcode(barcode);
        if (normalizedBarcode is null)
        {
            return null;
        }

        var products = await GetProductsAsync(cancellationToken).ConfigureAwait(false);
        var matches = products
            .Where(product => product.IsActive
                              && string.Equals(
                                  ProductValidator.NormalizeBarcode(product.Barcode),
                                  normalizedBarcode,
                                  StringComparison.Ordinal))
            .ToArray();

        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new DuplicateBarcodeException(
                normalizedBarcode,
                matches.Select(product => product.Id).ToArray()),
        };
    }

    public async Task SaveProductAsync(
        Product product,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MutateAtomicallyAsync(
                workbook =>
                {
                    var worksheet = workbook.Worksheet(ProductsSheet);
                    var products = ReadProducts(worksheet);
                    var existingProduct = products.SingleOrDefault(existing => existing.Id == product.Id);
                    var normalizedProduct = NormalizeProduct(product) with
                    {
                        SortOrder = existingProduct is null
                            ? GetNextSortOrder(products)
                            : product.SortOrder,
                        UpdatedAt = _timeProvider.GetUtcNow(),
                    };

                    var barcodeChanged = existingProduct is null
                        || !string.Equals(
                            ProductValidator.NormalizeBarcode(existingProduct.Barcode),
                            normalizedProduct.Barcode,
                            StringComparison.Ordinal);
                    var validation = barcodeChanged
                        ? _productValidator.Validate(normalizedProduct, products)
                        : _productValidator.Validate(normalizedProduct);
                    if (!validation.IsValid)
                    {
                        var messages = validation.Issues
                            .Where(issue => issue.Severity == ProductValidationSeverity.Error)
                            .Select(issue => issue.Message);
                        throw new ArgumentException(string.Join(" ", messages), nameof(product));
                    }

                    var rowNumber = FindProductRow(worksheet, normalizedProduct.Id)
                        ?? GetAppendRowNumber(worksheet);
                    WriteProduct(worksheet, rowNumber, normalizedProduct);
                    EnsureCategory(workbook.Worksheet(CategoriesSheet), normalizedProduct.Category);
                    SetMeta(workbook.Worksheet(MetaSheet), "UpdatedAt", _timeProvider.GetUtcNow().ToString("O"));
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveSaleAsync(
        Sale sale,
        IReadOnlyCollection<Product> inventoryUpdates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(inventoryUpdates);
        ValidateSale(sale);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MutateAtomicallyAsync(
                workbook => ApplySale(workbook, sale, inventoryUpdates),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ExcelMigrationResult> MigrateLegacyCoreAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(DatabasePath))
            {
                throw new IOException("The migration destination already exists.");
            }

            Directory.CreateDirectory(GetParentDirectory(DatabasePath));
            var temporaryPath = CreateTemporaryWorkbookPath(DatabasePath);
            var sourceHashBefore = await ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false);

            try
            {
                ExcelMigrationResult result;
                WorkbookPreservationSnapshot preservation;
                using (var workbook = new XLWorkbook(sourcePath))
                {
                    if (workbook.Worksheets.Any(sheet => ManagedSheetNames.Contains(sheet.Name)))
                    {
                        throw new InvalidDataException(
                            "The source already contains one or more SimplePOS managed sheet names.");
                    }

                    preservation = CapturePreservationSnapshot(workbook);
                    var mapping = FindLegacyMapping(workbook);
                    var migratedAt = _timeProvider.GetUtcNow();
                    var products = ReadLegacyProducts(mapping, migratedAt);

                    InitializeSchema(workbook, migratedAt);
                    var productsSheet = workbook.Worksheet(ProductsSheet);
                    for (var index = 0; index < products.Count; index++)
                    {
                        WriteProduct(productsSheet, index + 2, products[index]);
                    }

                    var meta = workbook.Worksheet(MetaSheet);
                    SetMeta(meta, "MigratedFrom", sourcePath);
                    SetMeta(meta, "LegacyWorksheet", mapping.Worksheet.Name);
                    SetMeta(meta, "LegacyHeaderRow", mapping.HeaderRow.ToString(CultureInfo.InvariantCulture));
                    SetMeta(meta, "LegacyNameColumn", mapping.NameColumn.ToString(CultureInfo.InvariantCulture));
                    SetMeta(meta, "LegacyPriceColumn", mapping.PriceColumn.ToString(CultureInfo.InvariantCulture));
                    SetMeta(meta, "LegacyBarcodeColumn", mapping.BarcodeColumn.ToString(CultureInfo.InvariantCulture));

                    workbook.SaveAs(temporaryPath);
                    result = BuildMigrationResult(sourcePath, DatabasePath, mapping, products);
                }

                cancellationToken.ThrowIfCancellationRequested();
                ValidateDatabaseWorkbook(temporaryPath, result.ProductCount, preservation);

                var sourceHashAfter = await ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false);
                if (!sourceHashBefore.Equals(sourceHashAfter, StringComparison.Ordinal))
                {
                    throw new ExcelDatabaseChangedException(sourcePath);
                }

                AtomicFileCommit.MoveNew(temporaryPath, DatabasePath);
                return result;
            }
            catch
            {
                AtomicFileCommit.TryDelete(temporaryPath);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MutateAtomicallyAsync(
        Action<XLWorkbook> mutation,
        CancellationToken cancellationToken)
    {
        EnsureDatabaseExists();
        var sourceHashBefore = await ComputeSha256Async(DatabasePath, cancellationToken).ConfigureAwait(false);
        var temporaryPath = CreateTemporaryWorkbookPath(DatabasePath);

        try
        {
            int productCount;
            WorkbookPreservationSnapshot preservation;
            cancellationToken.ThrowIfCancellationRequested();
            using (var workbook = new XLWorkbook(DatabasePath))
            {
                ValidateSchema(workbook);
                preservation = CapturePreservationSnapshot(workbook);
                mutation(workbook);
                productCount = CountProducts(workbook.Worksheet(ProductsSheet));
                workbook.SaveAs(temporaryPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ValidateDatabaseWorkbook(temporaryPath, productCount, preservation);

            var sourceHashAfter = await ComputeSha256Async(DatabasePath, cancellationToken).ConfigureAwait(false);
            if (!sourceHashBefore.Equals(sourceHashAfter, StringComparison.Ordinal))
            {
                throw new ExcelDatabaseChangedException(DatabasePath);
            }

            AtomicFileCommit.Replace(temporaryPath, DatabasePath);
        }
        catch
        {
            AtomicFileCommit.TryDelete(temporaryPath);
            throw;
        }
    }

    private void ApplySale(
        XLWorkbook workbook,
        Sale sale,
        IReadOnlyCollection<Product> inventoryUpdates)
    {
        var salesWorksheet = workbook.Worksheet(SalesSheet);
        if (ContainsId(salesWorksheet, columnNumber: 1, sale.Id))
        {
            throw new InvalidOperationException("The sale has already been recorded.");
        }

        var productsWorksheet = workbook.Worksheet(ProductsSheet);
        var products = ReadProducts(productsWorksheet).ToDictionary(product => product.Id);
        var rowsByProductId = GetProductRows(productsWorksheet);
        var updatesByProductId = new Dictionary<Guid, Product>();
        foreach (var update in inventoryUpdates)
        {
            if (!updatesByProductId.TryAdd(update.Id, update))
            {
                throw new ArgumentException("Inventory updates contain a duplicate product ID.", nameof(inventoryUpdates));
            }
        }

        var quantities = sale.Lines
            .GroupBy(line => line.ProductId)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0, (sum, line) => checked(sum + line.Quantity)));

        foreach (var (productId, quantity) in quantities)
        {
            if (!products.TryGetValue(productId, out var currentProduct))
            {
                throw new InvalidOperationException($"Product '{productId}' no longer exists.");
            }

            if (!currentProduct.IsActive)
            {
                throw new InvalidOperationException($"Product '{currentProduct.Name}' is inactive.");
            }

            if (currentProduct.Stock.HasValue)
            {
                if (!updatesByProductId.TryGetValue(productId, out var requestedUpdate)
                    || !requestedUpdate.Stock.HasValue)
                {
                    throw new InvalidOperationException(
                        $"A stock update is required for product '{currentProduct.Name}'.");
                }

                if (currentProduct.Stock.Value < quantity
                    && _outOfStockPolicy == OutOfStockPolicy.Block)
                {
                    throw new InvalidOperationException(
                        $"Insufficient stock for '{currentProduct.Name}'. "
                        + $"Available: {currentProduct.Stock.Value}, requested: {quantity}.");
                }

                var updatedProduct = currentProduct with
                {
                    Stock = Math.Max(0, currentProduct.Stock.Value - quantity),
                    UpdatedAt = _timeProvider.GetUtcNow(),
                };
                WriteProduct(productsWorksheet, rowsByProductId[productId], updatedProduct);
            }
            else if (updatesByProductId.ContainsKey(productId))
            {
                throw new InvalidOperationException(
                    $"Product '{currentProduct.Name}' does not use stock tracking.");
            }
        }

        var unexpectedUpdates = updatesByProductId.Keys
            .Where(productId => !quantities.ContainsKey(productId))
            .ToArray();
        if (unexpectedUpdates.Length > 0)
        {
            throw new ArgumentException(
                $"Inventory update '{unexpectedUpdates[0]}' is not part of the sale.",
                nameof(inventoryUpdates));
        }

        var salesRow = GetAppendRowNumber(salesWorksheet);
        salesWorksheet.Cell(salesRow, 1).SetValue(sale.Id.ToString("D"));
        salesWorksheet.Cell(salesRow, 2).SetValue(sale.CompletedAt.ToString("O"));
        salesWorksheet.Cell(salesRow, 3).SetValue(sale.Total);

        var saleItemsWorksheet = workbook.Worksheet(SaleItemsSheet);
        var saleItemRow = GetAppendRowNumber(saleItemsWorksheet);
        for (var index = 0; index < sale.Lines.Count; index++)
        {
            var line = sale.Lines[index];
            saleItemsWorksheet.Cell(saleItemRow, 1).SetValue(sale.Id.ToString("D"));
            saleItemsWorksheet.Cell(saleItemRow, 2).SetValue(index + 1);
            saleItemsWorksheet.Cell(saleItemRow, 3).SetValue(line.ProductId.ToString("D"));
            saleItemsWorksheet.Cell(saleItemRow, 4).SetValue(line.ProductName);
            saleItemsWorksheet.Cell(saleItemRow, 5).SetValue(line.UnitPrice);
            saleItemsWorksheet.Cell(saleItemRow, 6).SetValue(line.Quantity);
            saleItemsWorksheet.Cell(saleItemRow, 7).SetValue(line.LineTotal);
            saleItemRow++;
        }

        SetMeta(workbook.Worksheet(MetaSheet), "UpdatedAt", _timeProvider.GetUtcNow().ToString("O"));
    }

    private static void ValidateSale(Sale sale)
    {
        if (sale.Id == Guid.Empty)
        {
            throw new ArgumentException("Sale ID is required.", nameof(sale));
        }

        if (sale.Lines is null || sale.Lines.Count == 0)
        {
            throw new ArgumentException("A sale must contain at least one line.", nameof(sale));
        }

        long calculatedTotal = 0;
        foreach (var line in sale.Lines)
        {
            if (line.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(line.ProductName))
            {
                throw new ArgumentException("Every sale line must identify a product.", nameof(sale));
            }

            if (line.Quantity is < 1 or > 9_999)
            {
                throw new ArgumentException("Sale quantity must be between 1 and 9,999.", nameof(sale));
            }

            if (line.UnitPrice is < 0 or > ProductValidator.MaximumPrice)
            {
                throw new ArgumentException("Sale unit price is outside the supported range.", nameof(sale));
            }

            calculatedTotal = checked(calculatedTotal + line.LineTotal);
        }

        if (sale.Total != calculatedTotal)
        {
            throw new ArgumentException("Sale total does not match its lines.", nameof(sale));
        }
    }

    private static Product NormalizeProduct(Product product)
    {
        return product with
        {
            Name = ProductValidator.NormalizeText(product.Name),
            Barcode = ProductValidator.NormalizeBarcode(product.Barcode),
            Supplier = NormalizeOptionalText(product.Supplier),
            Category = NormalizeOptionalText(product.Category),
        };
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = ProductValidator.NormalizeText(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static int GetNextSortOrder(IReadOnlyCollection<Product> products)
    {
        return products.Count == 0 ? 0 : checked(products.Max(product => product.SortOrder) + 1);
    }

    private static void InitializeSchema(XLWorkbook workbook, DateTimeOffset timestamp)
    {
        var products = AddWorksheet(workbook, ProductsSheet, ProductHeaders);
        products.Column(3).Style.NumberFormat.Format = "0";
        products.Column(4).Style.NumberFormat.Format = "@";
        products.SheetView.FreezeRows(1);

        AddWorksheet(workbook, CategoriesSheet, CategoryHeaders);
        var meta = AddWorksheet(workbook, MetaSheet, MetaHeaders);
        SetMeta(meta, "DatabaseKind", DatabaseKind);
        SetMeta(meta, "SchemaVersion", CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
        SetMeta(meta, "CreatedAt", timestamp.ToString("O"));
        SetMeta(meta, "UpdatedAt", timestamp.ToString("O"));
        AddWorksheet(workbook, SalesSheet, SaleHeaders);
        AddWorksheet(workbook, SaleItemsSheet, SaleItemHeaders);
    }

    private static IXLWorksheet AddWorksheet(
        XLWorkbook workbook,
        string name,
        IReadOnlyList<string> headers)
    {
        var worksheet = workbook.Worksheets.Add(name);
        for (var index = 0; index < headers.Count; index++)
        {
            worksheet.Cell(1, index + 1).SetValue(headers[index]);
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Count);
        headerRange.Style.Font.Bold = true;
        return worksheet;
    }

    private static void WriteProduct(IXLWorksheet worksheet, int rowNumber, Product product)
    {
        worksheet.Cell(rowNumber, 1).SetValue(product.Id.ToString("D"));
        worksheet.Cell(rowNumber, 2).SetValue(product.Name);
        worksheet.Cell(rowNumber, 3).SetValue(product.Price);
        SetOptionalText(worksheet.Cell(rowNumber, 4), product.Barcode);
        SetOptionalText(worksheet.Cell(rowNumber, 5), product.Supplier);
        SetOptionalNumber(worksheet.Cell(rowNumber, 6), product.Stock);
        SetOptionalText(worksheet.Cell(rowNumber, 7), product.Category);
        worksheet.Cell(rowNumber, 8).SetValue(product.QuickAdd);
        worksheet.Cell(rowNumber, 9).SetValue(product.SortOrder);
        worksheet.Cell(rowNumber, 10).SetValue(product.IsActive);
        worksheet.Cell(rowNumber, 11).SetValue(product.UpdatedAt.ToString("O"));
    }

    private static void SetOptionalText(IXLCell cell, string? value)
    {
        if (value is null)
        {
            cell.Clear(XLClearOptions.Contents);
        }
        else
        {
            cell.SetValue(value);
        }
    }

    private static void SetOptionalNumber(IXLCell cell, int? value)
    {
        if (value.HasValue)
        {
            cell.SetValue(value.Value);
        }
        else
        {
            cell.Clear(XLClearOptions.Contents);
        }
    }

    private static IReadOnlyList<Product> ReadProducts(IXLWorksheet worksheet)
    {
        var products = new List<Product>();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var identifiers = new HashSet<Guid>();

        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var idText = GetText(worksheet.Cell(rowNumber, 1));
            if (idText.Length == 0 && worksheet.Row(rowNumber).IsEmpty())
            {
                continue;
            }

            if (!Guid.TryParse(idText, out var id) || id == Guid.Empty)
            {
                throw new InvalidDataException($"Products row {rowNumber} has an invalid ProductId.");
            }

            if (!identifiers.Add(id))
            {
                throw new InvalidDataException($"Products contains duplicate ProductId '{id}'.");
            }

            var name = ProductValidator.NormalizeText(GetText(worksheet.Cell(rowNumber, 2)));
            if (name.Length == 0)
            {
                throw new InvalidDataException($"Products row {rowNumber} has no name.");
            }

            var price = ReadInt64(worksheet.Cell(rowNumber, 3), "Price", rowNumber);
            var stock = ReadOptionalInt32(worksheet.Cell(rowNumber, 6), "Stock", rowNumber);
            var sortOrder = ReadInt32(worksheet.Cell(rowNumber, 9), "SortOrder", rowNumber);
            var updatedAt = ReadDateTimeOffset(worksheet.Cell(rowNumber, 11), "UpdatedAt", rowNumber);

            var product = new Product
            {
                Id = id,
                Name = name,
                Price = price,
                Barcode = ProductValidator.NormalizeBarcode(GetText(worksheet.Cell(rowNumber, 4))),
                Supplier = NormalizeOptionalText(GetText(worksheet.Cell(rowNumber, 5))),
                Stock = stock,
                Category = NormalizeOptionalText(GetText(worksheet.Cell(rowNumber, 7))),
                QuickAdd = ReadBoolean(worksheet.Cell(rowNumber, 8), "QuickAdd", rowNumber),
                SortOrder = sortOrder,
                IsActive = ReadBoolean(worksheet.Cell(rowNumber, 10), "IsActive", rowNumber),
                UpdatedAt = updatedAt,
            };

            var validation = new ProductValidator().Validate(product);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(
                    $"Products row {rowNumber} is invalid: "
                    + string.Join(" ", validation.Issues.Select(issue => issue.Message)));
            }

            products.Add(product);
        }

        return products;
    }

    private static int? FindProductRow(IXLWorksheet worksheet, Guid productId)
    {
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            if (Guid.TryParse(GetText(worksheet.Cell(rowNumber, 1)), out var id) && id == productId)
            {
                return rowNumber;
            }
        }

        return null;
    }

    private static Dictionary<Guid, int> GetProductRows(IXLWorksheet worksheet)
    {
        var result = new Dictionary<Guid, int>();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            if (Guid.TryParse(GetText(worksheet.Cell(rowNumber, 1)), out var id))
            {
                result.Add(id, rowNumber);
            }
        }

        return result;
    }

    private static void EnsureCategory(IXLWorksheet worksheet, string? category)
    {
        if (category is null)
        {
            return;
        }

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            if (string.Equals(
                    ProductValidator.NormalizeText(GetText(worksheet.Cell(rowNumber, 1))),
                    category,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var appendRow = GetAppendRowNumber(worksheet);
        worksheet.Cell(appendRow, 1).SetValue(category);
        worksheet.Cell(appendRow, 2).SetValue(appendRow - 2);
    }

    private static void SetMeta(IXLWorksheet worksheet, string key, string value)
    {
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            if (string.Equals(GetText(worksheet.Cell(rowNumber, 1)), key, StringComparison.Ordinal))
            {
                worksheet.Cell(rowNumber, 2).SetValue(value);
                return;
            }
        }

        var appendRow = GetAppendRowNumber(worksheet);
        worksheet.Cell(appendRow, 1).SetValue(key);
        worksheet.Cell(appendRow, 2).SetValue(value);
    }

    private static int GetAppendRowNumber(IXLWorksheet worksheet)
    {
        return Math.Max(2, (worksheet.LastRowUsed()?.RowNumber() ?? 1) + 1);
    }

    private static int CountProducts(IXLWorksheet worksheet)
    {
        return ReadProducts(worksheet).Count;
    }

    private static bool ContainsId(IXLWorksheet worksheet, int columnNumber, Guid id)
    {
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            if (Guid.TryParse(GetText(worksheet.Cell(rowNumber, columnNumber)), out var candidate)
                && candidate == id)
            {
                return true;
            }
        }

        return false;
    }

    private static LegacyMapping FindLegacyMapping(XLWorkbook workbook)
    {
        var candidates = new List<LegacyMapping>();
        foreach (var worksheet in workbook.Worksheets)
        {
            var lastRow = Math.Min(worksheet.LastRowUsed()?.RowNumber() ?? 0, 50);
            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (var rowNumber = 1; rowNumber <= lastRow; rowNumber++)
            {
                int? nameColumn = null;
                int? priceColumn = null;
                int? barcodeColumn = null;
                for (var columnNumber = 1; columnNumber <= lastColumn; columnNumber++)
                {
                    var header = NormalizeHeader(GetText(worksheet.Cell(rowNumber, columnNumber)));
                    if (LegacyNameHeaders.Contains(header))
                    {
                        nameColumn = columnNumber;
                    }
                    else if (LegacyPriceHeaders.Contains(header))
                    {
                        priceColumn = columnNumber;
                    }
                    else if (LegacyBarcodeHeaders.Contains(header))
                    {
                        barcodeColumn = columnNumber;
                    }
                }

                if (nameColumn.HasValue && priceColumn.HasValue && barcodeColumn.HasValue)
                {
                    candidates.Add(new LegacyMapping(
                        worksheet,
                        rowNumber,
                        nameColumn.Value,
                        priceColumn.Value,
                        barcodeColumn.Value));
                }
            }
        }

        return candidates.Count switch
        {
            0 => throw new InvalidDataException(
                "No worksheet row contains recognizable name, price, and barcode headers."),
            1 => candidates[0],
            _ => throw new InvalidDataException(
                "More than one possible legacy header row was found; select the mapping explicitly."),
        };
    }

    private static IReadOnlyList<Product> ReadLegacyProducts(
        LegacyMapping mapping,
        DateTimeOffset migratedAt)
    {
        var products = new List<Product>();
        var lastRow = mapping.Worksheet.LastRowUsed()?.RowNumber() ?? mapping.HeaderRow;
        for (var rowNumber = mapping.HeaderRow + 1; rowNumber <= lastRow; rowNumber++)
        {
            var nameText = GetText(mapping.Worksheet.Cell(rowNumber, mapping.NameColumn));
            var priceCell = mapping.Worksheet.Cell(rowNumber, mapping.PriceColumn);
            var barcodeText = GetText(mapping.Worksheet.Cell(rowNumber, mapping.BarcodeColumn));
            if (string.IsNullOrWhiteSpace(nameText)
                && string.IsNullOrWhiteSpace(GetText(priceCell))
                && string.IsNullOrWhiteSpace(barcodeText))
            {
                continue;
            }

            var name = ProductValidator.NormalizeText(nameText);
            if (name.Length == 0)
            {
                throw new InvalidDataException($"Legacy row {rowNumber} contains data but has no product name.");
            }

            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = name,
                Price = ReadLegacyPrice(priceCell, rowNumber),
                Barcode = ProductValidator.NormalizeBarcode(barcodeText),
                QuickAdd = false,
                SortOrder = products.Count,
                IsActive = true,
                UpdatedAt = migratedAt,
            };

            var validation = new ProductValidator().Validate(product);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(
                    $"Legacy row {rowNumber} is invalid: "
                    + string.Join(" ", validation.Issues.Select(issue => issue.Message)));
            }

            products.Add(product);
        }

        if (products.Count == 0)
        {
            throw new InvalidDataException("The legacy worksheet contains no product rows.");
        }

        return products;
    }

    private static long ReadLegacyPrice(IXLCell cell, int rowNumber)
    {
        if (cell.TryGetValue<long>(out var numericValue))
        {
            return numericValue;
        }

        var text = GetText(cell)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("₩", string.Empty, StringComparison.Ordinal)
            .Replace("원", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException($"Legacy row {rowNumber} has an invalid price.");
        }

        return parsed;
    }

    private static ExcelMigrationResult BuildMigrationResult(
        string sourcePath,
        string destinationPath,
        LegacyMapping mapping,
        IReadOnlyList<Product> products)
    {
        var barcodeGroups = products
            .Select(product => ProductValidator.NormalizeBarcode(product.Barcode))
            .Where(barcode => barcode is not null)
            .GroupBy(barcode => barcode!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToArray();
        var nameGroups = products
            .GroupBy(product => ProductValidator.NormalizeText(product.Name), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToArray();

        return new ExcelMigrationResult(
            sourcePath,
            destinationPath,
            mapping.Worksheet.Name,
            mapping.HeaderRow,
            mapping.NameColumn,
            mapping.PriceColumn,
            mapping.BarcodeColumn,
            products.Count,
            products.Count(product => ProductValidator.NormalizeBarcode(product.Barcode) is null),
            barcodeGroups.Sum(group => group.Count() - 1),
            barcodeGroups.Length,
            nameGroups.Sum(group => group.Count() - 1),
            nameGroups.Length,
            products.Count(product => product.Price > ProductValidator.HighPriceWarningThreshold));
    }

    private static void ValidateDatabaseWorkbook(
        string path,
        int expectedProductCount,
        WorkbookPreservationSnapshot? preservation)
    {
        try
        {
            using var workbook = new XLWorkbook(path);
            ValidateSchema(workbook);
            var actualProductCount = CountProducts(workbook.Worksheet(ProductsSheet));
            if (actualProductCount != expectedProductCount)
            {
                throw new InvalidDataException(
                    $"Candidate workbook contains {actualProductCount} products; expected {expectedProductCount}.");
            }

            ValidateSaleHistory(workbook);
            if (preservation is not null)
            {
                var candidateSnapshot = CapturePreservationSnapshot(workbook);
                var candidateFingerprints = candidateSnapshot.Worksheets
                    .ToDictionary(item => item.Name, item => item.Fingerprint, StringComparer.Ordinal);
                if (preservation.Worksheets.Any(item =>
                        !candidateFingerprints.TryGetValue(item.Name, out var fingerprint)
                        || !string.Equals(item.Fingerprint, fingerprint, StringComparison.Ordinal)))
                {
                    throw new InvalidDataException(
                        "Candidate workbook did not preserve unmanaged worksheet content.");
                }
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or FormatException or ArgumentException)
        {
            throw new InvalidDataException("The candidate workbook failed validation.", exception);
        }
    }

    private static void ValidateSchema(XLWorkbook workbook)
    {
        ValidateHeaders(GetRequiredWorksheet(workbook, ProductsSheet), ProductHeaders);
        ValidateHeaders(GetRequiredWorksheet(workbook, CategoriesSheet), CategoryHeaders);
        var meta = GetRequiredWorksheet(workbook, MetaSheet);
        ValidateHeaders(meta, MetaHeaders);
        ValidateHeaders(GetRequiredWorksheet(workbook, SalesSheet), SaleHeaders);
        ValidateHeaders(GetRequiredWorksheet(workbook, SaleItemsSheet), SaleItemHeaders);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lastRow = meta.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var key = GetText(meta.Cell(rowNumber, 1));
            if (key.Length > 0 && !values.TryAdd(key, GetText(meta.Cell(rowNumber, 2))))
            {
                throw new InvalidDataException($"Meta contains duplicate key '{key}'.");
            }
        }

        if (!values.TryGetValue("DatabaseKind", out var databaseKind)
            || !string.Equals(databaseKind, DatabaseKind, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The workbook is not a SimplePOS database.");
        }

        if (!values.TryGetValue("SchemaVersion", out var schemaVersion)
            || !int.TryParse(schemaVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || version != CurrentSchemaVersion)
        {
            throw new InvalidDataException("The workbook schema version is unsupported.");
        }
    }

    private static IXLWorksheet GetRequiredWorksheet(XLWorkbook workbook, string name)
    {
        if (!workbook.TryGetWorksheet(name, out var worksheet))
        {
            throw new InvalidDataException($"Required worksheet '{name}' is missing.");
        }

        return worksheet;
    }

    private static void ValidateHeaders(IXLWorksheet worksheet, IReadOnlyList<string> expectedHeaders)
    {
        for (var index = 0; index < expectedHeaders.Count; index++)
        {
            var actual = GetText(worksheet.Cell(1, index + 1));
            if (!string.Equals(actual, expectedHeaders[index], StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Worksheet '{worksheet.Name}' column {index + 1} must be '{expectedHeaders[index]}'.");
            }
        }
    }

    private static void ValidateSaleHistory(XLWorkbook workbook)
    {
        var sales = workbook.Worksheet(SalesSheet);
        var saleIds = new HashSet<Guid>();
        var lastSaleRow = sales.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastSaleRow; rowNumber++)
        {
            if (!Guid.TryParse(GetText(sales.Cell(rowNumber, 1)), out var saleId)
                || saleId == Guid.Empty
                || !saleIds.Add(saleId))
            {
                throw new InvalidDataException($"Sales row {rowNumber} has an invalid or duplicate SaleId.");
            }
        }

        var saleItems = workbook.Worksheet(SaleItemsSheet);
        var lastItemRow = saleItems.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastItemRow; rowNumber++)
        {
            if (!Guid.TryParse(GetText(saleItems.Cell(rowNumber, 1)), out var saleId)
                || !saleIds.Contains(saleId))
            {
                throw new InvalidDataException($"SaleItems row {rowNumber} references an unknown sale.");
            }
        }
    }

    private static WorkbookPreservationSnapshot CapturePreservationSnapshot(XLWorkbook workbook)
    {
        var worksheets = new List<WorksheetContentFingerprint>();
        foreach (var worksheet in workbook.Worksheets)
        {
            if (!ManagedSheetNames.Contains(worksheet.Name))
            {
                worksheets.Add(new WorksheetContentFingerprint(
                    worksheet.Name,
                    FingerprintCells(worksheet.CellsUsed())));
                continue;
            }

            var ownedColumnCount = GetOwnedColumnCount(worksheet.Name);
            var extraCells = worksheet.CellsUsed()
                .Where(cell => cell.Address.ColumnNumber > ownedColumnCount);
            worksheets.Add(new WorksheetContentFingerprint(
                $"{worksheet.Name}:extra-columns",
                FingerprintCells(extraCells)));
        }

        return new WorkbookPreservationSnapshot(worksheets.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray());
    }

    private static string FingerprintCells(IEnumerable<IXLCell> cells)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var cell in cells.OrderBy(cell => cell.Address.RowNumber)
                     .ThenBy(cell => cell.Address.ColumnNumber))
        {
            var record = string.Join(
                '\u001f',
                cell.Address.ToStringRelative(),
                cell.DataType.ToString(),
                cell.GetFormattedString(),
                cell.FormulaA1 ?? string.Empty,
                cell.Style.NumberFormat.Format ?? string.Empty);
            hash.AppendData(Encoding.UTF8.GetBytes(record));
            hash.AppendData([0]);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static int GetOwnedColumnCount(string worksheetName)
    {
        return worksheetName switch
        {
            ProductsSheet => ProductHeaders.Length,
            CategoriesSheet => CategoryHeaders.Length,
            MetaSheet => MetaHeaders.Length,
            SalesSheet => SaleHeaders.Length,
            SaleItemsSheet => SaleItemHeaders.Length,
            _ => 0,
        };
    }

    private static long ReadInt64(IXLCell cell, string fieldName, int rowNumber)
    {
        if (cell.TryGetValue<long>(out var value))
        {
            return value;
        }

        var text = GetText(cell).Replace(",", string.Empty, StringComparison.Ordinal);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        throw new InvalidDataException($"Products row {rowNumber} has an invalid {fieldName}.");
    }

    private static int ReadInt32(IXLCell cell, string fieldName, int rowNumber)
    {
        var value = ReadInt64(cell, fieldName, rowNumber);
        if (value is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidDataException($"Products row {rowNumber} has an invalid {fieldName}.");
        }

        return (int)value;
    }

    private static int? ReadOptionalInt32(IXLCell cell, string fieldName, int rowNumber)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        return ReadInt32(cell, fieldName, rowNumber);
    }

    private static bool ReadBoolean(IXLCell cell, string fieldName, int rowNumber)
    {
        if (cell.TryGetValue<bool>(out var value))
        {
            return value;
        }

        if (bool.TryParse(GetText(cell), out value))
        {
            return value;
        }

        throw new InvalidDataException($"Products row {rowNumber} has an invalid {fieldName}.");
    }

    private static DateTimeOffset ReadDateTimeOffset(IXLCell cell, string fieldName, int rowNumber)
    {
        if (DateTimeOffset.TryParse(
                GetText(cell),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var value))
        {
            return value;
        }

        throw new InvalidDataException($"Products row {rowNumber} has an invalid {fieldName}.");
    }

    private static string GetText(IXLCell cell)
    {
        return cell.GetFormattedString().Trim();
    }

    private static string NormalizeHeader(string value)
    {
        return string.Concat(ProductValidator.NormalizeText(value).Where(char.IsLetterOrDigit))
            .ToLowerInvariant();
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string CreateTemporaryWorkbookPath(string destinationPath)
    {
        return Path.Combine(
            GetParentDirectory(destinationPath),
            $".{Path.GetFileNameWithoutExtension(destinationPath)}.{Guid.NewGuid():N}.tmp.xlsx");
    }

    private void EnsureDatabaseExists()
    {
        if (!File.Exists(DatabasePath))
        {
            throw new FileNotFoundException("The database workbook does not exist.", DatabasePath);
        }
    }

    private static string GetParentDirectory(string path)
    {
        return Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The workbook path has no parent directory.");
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record LegacyMapping(
        IXLWorksheet Worksheet,
        int HeaderRow,
        int NameColumn,
        int PriceColumn,
        int BarcodeColumn);

    private sealed record WorksheetContentFingerprint(string Name, string Fingerprint);

    private sealed record WorkbookPreservationSnapshot(
        IReadOnlyList<WorksheetContentFingerprint> Worksheets);
}
