using System.Security.Cryptography;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using Pos.Core;
using Pos.Infrastructure;
using OxmlColumn = DocumentFormat.OpenXml.Spreadsheet.Column;
using OxmlColumns = DocumentFormat.OpenXml.Spreadsheet.Columns;
using OxmlSheet = DocumentFormat.OpenXml.Spreadsheet.Sheet;
using OxmlSheetData = DocumentFormat.OpenXml.Spreadsheet.SheetData;

namespace Pos.Tests;

public sealed class ExcelIntegrationTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SimplePOS.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Migration_ReproducesCatalogQualityCounts_WithoutChangingSource()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var source = FindSampleWorkbook();
        var destination = Path.Combine(_temporaryDirectory, "catalog_pos.xlsx");
        var originalHash = await HashAsync(source);

        var result = await ExcelPosRepository.MigrateLegacyAsync(source, destination);

        Assert.Equal(617, result.ProductCount);
        Assert.Equal(3, result.BlankBarcodeCount);
        Assert.Equal(12, result.DuplicateBarcodeRowCount);
        Assert.True(result.DuplicateBarcodeGroupCount > 0);
        Assert.Equal(17, result.DuplicateNameRowCount);
        Assert.True(result.DuplicateNameGroupCount > 0);
        Assert.Equal(1, result.HighPriceCount);
        Assert.Equal(originalHash, await HashAsync(source));

        var repository = new ExcelPosRepository(destination);
        var products = await repository.GetProductsAsync();
        Assert.Equal(617, products.Count);

        var duplicateBarcode = products
            .Where(product => !string.IsNullOrWhiteSpace(product.Barcode))
            .GroupBy(product => product.Barcode, StringComparer.Ordinal)
            .First(group => group.Count() > 1)
            .Key!;
        await Assert.ThrowsAsync<DuplicateBarcodeException>(
            () => repository.FindProductByBarcodeAsync(duplicateBarcode));
    }

    [Fact]
    public async Task Sale_AtomicallyWritesHistoryAndDeductsStock()
    {
        var databasePath = await CreateDatabaseAsync();
        var repository = new ExcelPosRepository(databasePath);
        var product = new Product
        {
            Name = "테스트 상품",
            Price = 2_500,
            Barcode = "0001234567890",
            Stock = 3,
            QuickAdd = true,
        };
        await repository.SaveProductAsync(product);

        var cart = new CartService();
        Assert.True(cart.Add(product, quantity: 2).Succeeded);
        var sale = await new SaleService(repository).CompleteSaleAsync(cart);

        Assert.True(cart.IsEmpty);
        Assert.Equal(5_000, sale.Total);
        var reloaded = Assert.Single(await repository.GetProductsAsync());
        Assert.Equal(1, reloaded.Stock);

        using var workbook = new XLWorkbook(databasePath);
        var saleRow = workbook.Worksheet("Sales").Row(2);
        var itemRow = workbook.Worksheet("SaleItems").Row(2);
        Assert.Equal(sale.Id.ToString("D"), saleRow.Cell(1).GetString());
        Assert.Equal(5_000, saleRow.Cell(3).GetValue<long>());
        Assert.Equal(product.Id.ToString("D"), itemRow.Cell(3).GetString());
        Assert.Equal(2, itemRow.Cell(6).GetValue<int>());
    }

    [Fact]
    public async Task Sale_BlockPolicyPreservesCartButWarnPolicyClampsStockToZero()
    {
        var databasePath = await CreateDatabaseAsync();
        var blockRepository = new ExcelPosRepository(databasePath, OutOfStockPolicy.Block);
        var product = new Product { Name = "재고 상품", Price = 1_000, Stock = 1 };
        await blockRepository.SaveProductAsync(product);

        var staleProduct = product with { Stock = 2 };
        var blockedCart = new CartService();
        Assert.True(blockedCart.Add(staleProduct, quantity: 2).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SaleService(blockRepository).CompleteSaleAsync(blockedCart));
        Assert.False(blockedCart.IsEmpty);
        Assert.Equal(1, Assert.Single(await blockRepository.GetProductsAsync()).Stock);

        var warnRepository = new ExcelPosRepository(databasePath, OutOfStockPolicy.WarnAndAllow);
        var warnCart = new CartService();
        Assert.True(warnCart.Add(
            product,
            quantity: 2,
            OutOfStockPolicy.WarnAndAllow,
            outOfStockConfirmed: true).Succeeded);
        await new SaleService(warnRepository).CompleteSaleAsync(warnCart);

        Assert.True(warnCart.IsEmpty);
        Assert.Equal(0, Assert.Single(await warnRepository.GetProductsAsync()).Stock);
    }

    [Fact]
    public async Task Sale_AllowPolicyClampsStockToZeroWithoutConfirmation()
    {
        var databasePath = await CreateDatabaseAsync();
        var repository = new ExcelPosRepository(databasePath, OutOfStockPolicy.Allow);
        var product = new Product { Name = "즉시 허용 상품", Price = 1_000, Stock = 1 };
        await repository.SaveProductAsync(product);
        var cart = new CartService();

        var added = cart.Add(product, quantity: 2, OutOfStockPolicy.Allow);
        await new SaleService(repository).CompleteSaleAsync(cart);

        Assert.True(added.Succeeded);
        Assert.True(cart.IsEmpty);
        Assert.Equal(0, Assert.Single(await repository.GetProductsAsync()).Stock);
    }

    [Fact]
    public async Task ProductSave_PreservesUnmanagedSheetFormulaAndFormatting()
    {
        var databasePath = await CreateDatabaseAsync();
        using (var workbook = new XLWorkbook(databasePath))
        {
            var unmanaged = workbook.AddWorksheet("운영메모");
            unmanaged.Cell("A1").Value = 40;
            unmanaged.Cell("A2").Value = 2;
            unmanaged.Cell("A3").FormulaA1 = "=SUM(A1:A2)";
            unmanaged.Cell("A3").Style.Fill.BackgroundColor = XLColor.Yellow;
            workbook.Save();
        }

        var repository = new ExcelPosRepository(databasePath);
        await repository.SaveProductAsync(new Product { Name = "보존 검사", Price = 100 });

        using var reloaded = new XLWorkbook(databasePath);
        var sheet = reloaded.Worksheet("운영메모");
        Assert.Equal("SUM(A1:A2)", sheet.Cell("A3").FormulaA1);
        Assert.Equal(XLColor.Yellow.Color.ToArgb(), sheet.Cell("A3").Style.Fill.BackgroundColor.Color.ToArgb());
    }

    [Fact]
    public async Task ProductSave_NormalizesOverlappingColumnDefinitions()
    {
        var databasePath = await CreateDatabaseAsync();
        AddOverlappingProductColumns(databasePath);

        double expectedFirstColumnWidth;
        double expectedSecondColumnWidth;
        using (var workbook = new XLWorkbook(databasePath))
        {
            var products = workbook.Worksheet("Products");
            expectedFirstColumnWidth = products.Column(1).Width;
            expectedSecondColumnWidth = products.Column(2).Width;
        }

        var product = new Product
        {
            Name = "겹친 열 정의 회귀 검사",
            Price = 1_234,
            Barcode = "TEST-OVERLAPPING-COLUMNS",
        };
        var repository = new ExcelPosRepository(databasePath);

        await repository.SaveProductAsync(product);

        var reloadedProducts = await repository.GetProductsAsync();
        Assert.Equal(product.Id, Assert.Single(reloadedProducts).Id);

        using (var workbook = new XLWorkbook(databasePath))
        {
            var products = workbook.Worksheet("Products");
            Assert.Equal(expectedFirstColumnWidth, products.Column(1).Width, 6);
            Assert.Equal(expectedSecondColumnWidth, products.Column(2).Width, 6);
        }

        var ranges = ReadProductColumnRanges(databasePath).OrderBy(range => range.Min).ToArray();
        Assert.Equal(ranges.Length, ranges.Select(range => range.Min).Distinct().Count());
        for (var index = 1; index < ranges.Length; index++)
        {
            Assert.True(ranges[index - 1].Max < ranges[index].Min);
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

    private async Task<string> CreateDatabaseAsync()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, $"database-{Guid.NewGuid():N}.xlsx");
        await new ExcelPosRepository(path).CreateAsync();
        return path;
    }

    private static void AddOverlappingProductColumns(string path)
    {
        using var document = SpreadsheetDocument.Open(path, isEditable: true);
        var workbookPart = document.WorkbookPart!;
        var productSheet = workbookPart.Workbook.Sheets!
            .Elements<OxmlSheet>()
            .Single(sheet => string.Equals(sheet.Name?.Value, "Products", StringComparison.Ordinal));
        var relationshipId = productSheet.Id?.Value
            ?? throw new InvalidDataException("Products sheet has no relationship ID.");
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(relationshipId);
        var worksheet = worksheetPart.Worksheet;
        var columns = worksheet.GetFirstChild<OxmlColumns>();
        if (columns is null)
        {
            var sheetData = worksheet.GetFirstChild<OxmlSheetData>()
                ?? throw new InvalidDataException("Products sheet has no sheet data.");
            columns = worksheet.InsertBefore(new OxmlColumns(), sheetData);
        }

        columns.PrependChild(new OxmlColumn
        {
            Min = 1U,
            Max = 2U,
            Width = 9.140625D,
            CustomWidth = true,
        });
        columns.AppendChild(new OxmlColumn
        {
            Min = 1U,
            Max = 1U,
            Width = 40D,
            CustomWidth = true,
        });
        columns.AppendChild(new OxmlColumn
        {
            Min = 2U,
            Max = 2U,
            Width = 60D,
            CustomWidth = true,
        });
        worksheet.Save();
    }

    private static IReadOnlyList<(uint Min, uint Max)> ReadProductColumnRanges(string path)
    {
        using var document = SpreadsheetDocument.Open(path, isEditable: false);
        var workbookPart = document.WorkbookPart!;
        var productSheet = workbookPart.Workbook.Sheets!
            .Elements<OxmlSheet>()
            .Single(sheet => string.Equals(sheet.Name?.Value, "Products", StringComparison.Ordinal));
        var relationshipId = productSheet.Id?.Value
            ?? throw new InvalidDataException("Products sheet has no relationship ID.");
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(relationshipId);
        return worksheetPart.Worksheet
            .Elements<OxmlColumns>()
            .SelectMany(columns => columns.Elements<OxmlColumn>())
            .Select(column => (column.Min!.Value, column.Max!.Value))
            .ToArray();
    }

    private static string FindSampleWorkbook()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var outputDirectory = Path.Combine(directory.FullName, "outputs");
            if (Directory.Exists(outputDirectory))
            {
                var match = Directory.EnumerateFiles(outputDirectory, "상품목록.xlsx", SearchOption.AllDirectories).SingleOrDefault();
                if (match is not null)
                {
                    return match;
                }
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("테스트용 상품목록.xlsx를 찾지 못했습니다.");
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
}
