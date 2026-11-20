using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClosedXML.Excel;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.Desktop.Tests;

public sealed class SalesWorkflowTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SimplePOS.Desktop.Tests",
        Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public async Task ScannerWithTabTerminatorAddsProductFromButtonFocus()
    {
        var repository = new ExcelPosRepository(await CreateDatabaseAsync());
        var product = new Product { Name = "보리차", Barcode = "00042", Price = 1200 };
        await repository.SaveProductAsync(product);
        var settings = new AppSettings { ScannerTerminatorKey = "Tab" };
        var sounds = 0;
        var window = new SalesWindow(repository, settings, _ => Task.FromResult<IPosRepository>(repository),
            () => sounds++, new BackupService());
        window.Show();
        try
        {
            await WaitForAsync(() => window.FindControl<TextBlock>("DatabaseStatusText")!.Text!.Contains("판매 준비"));
            Dispatcher.UIThread.RunJobs();
            var button = window.FindControl<Button>("SettingsButton")!;
            button.Focus();
            button.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = product.Barcode });
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab });
            await WaitForAsync(() => sounds == 1);
            Assert.Equal("1개", window.FindControl<TextBlock>("TotalItemCountText")!.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task BarcodeEnterAndConfirmedSalePersistStockAndHistory()
    {
        var databasePath = await CreateDatabaseAsync();
        var repository = new ExcelPosRepository(databasePath);
        var product = new Product
        {
            Name = "테스트 보리차",
            Barcode = "0001234567890",
            Price = 1_500,
            Stock = 3,
            QuickAdd = true,
        };
        await repository.SaveProductAsync(product);
        var window = new SalesWindow(repository, OutOfStockPolicy.Block);
        await window.InitializeAsync();
        window.Show();

        try
        {
            var barcodeInput = window.FindControl<TextBox>("BarcodeInput")!;
            barcodeInput.Text = product.Barcode;
            barcodeInput.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                Source = barcodeInput,
            });
            await WaitForAsync(() => window.FindControl<TextBlock>("TotalItemCountText")!.Text!.Contains('1'));
            Click(FindCartButton(window, "+"));
            await WaitForAsync(() => window.FindControl<TextBlock>("TotalItemCountText")!.Text!.Contains('2'));
            Click(FindCartButton(window, "−"));
            await WaitForAsync(() => window.FindControl<TextBlock>("TotalItemCountText")!.Text!.Contains('1'));

            Click(window.FindControl<Button>("CompleteSaleButton")!);
            await WaitForAsync(() => window.FindControl<Control>("SaleConfirmationPanel")!.IsVisible);
            Click(window.FindControl<Button>("CancelSaleButton")!);
            await WaitForAsync(() => !window.FindControl<Control>("SaleConfirmationPanel")!.IsVisible);
            Assert.Contains('1', window.FindControl<TextBlock>("TotalItemCountText")!.Text!);
            using (var cancelledWorkbook = new XLWorkbook(databasePath))
            {
                Assert.Equal(1, cancelledWorkbook.Worksheet("Sales").LastRowUsed()!.RowNumber());
            }

            Click(window.FindControl<Button>("CompleteSaleButton")!);
            await WaitForAsync(() => window.FindControl<Control>("SaleConfirmationPanel")!.IsVisible);
            Click(window.FindControl<Button>("ConfirmSaleButton")!);
            await WaitForAsync(() => window.FindControl<TextBlock>("TotalItemCountText")!.Text!.Contains('0'));

            var reopenedProduct = Assert.Single(await new ExcelPosRepository(databasePath).GetProductsAsync());
            Assert.Equal(2, reopenedProduct.Stock);
            using var workbook = new XLWorkbook(databasePath);
            Assert.Equal(1_500L, workbook.Worksheet("Sales").Cell("C2").GetValue<long>());
            Assert.Equal(product.Id.ToString("D"), workbook.Worksheet("SaleItems").Cell("C2").GetString());
            Assert.Equal(1, workbook.Worksheet("SaleItems").Cell("F2").GetValue<int>());

            var reopenedWindow = new SalesWindow(new ExcelPosRepository(databasePath), OutOfStockPolicy.Block);
            await reopenedWindow.InitializeAsync();
            reopenedWindow.Show();
            try
            {
                var quickAdd = Assert.Single(reopenedWindow.FindControl<WrapPanel>("QuickAddPanel")!.Children.OfType<Button>());
                Assert.Contains("재고 2", quickAdd.Content?.ToString());
            }
            finally
            {
                reopenedWindow.Close();
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ConsecutiveDelayedScansKeepCheckoutDisabledUntilBothAreApplied()
    {
        var product = new Product
        {
            Name = "연속 스캔 상품",
            Barcode = "DELAYED-SCAN-001",
            Price = 1_200,
            Stock = 10,
        };
        var repository = new DelayedLookupRepository(product);
        var window = new SalesWindow(repository, OutOfStockPolicy.Block);
        await window.InitializeAsync();
        window.Show();

        try
        {
            RaiseScan(window, product.Barcode);
            await repository.FirstLookupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            RaiseScan(window, product.Barcode);
            Assert.False(window.FindControl<Button>("CompleteSaleButton")!.IsEnabled);

            repository.AllowFirstLookup.TrySetResult();
            await repository.SecondLookupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForAsync(() => window.FindControl<TextBlock>("TotalItemCountText")!.Text == "1개");
            Assert.False(window.FindControl<Button>("CompleteSaleButton")!.IsEnabled);

            repository.AllowSecondLookup.TrySetResult();
            await WaitForAsync(() => window.FindControl<TextBlock>("TotalItemCountText")!.Text == "2개");
            Assert.True(window.FindControl<Button>("CompleteSaleButton")!.IsEnabled);
            Click(window.FindControl<Button>("CompleteSaleButton")!);
            Assert.Equal("상품 2개 · 2,400원", window.FindControl<TextBlock>("ConfirmationSummary")!.Text);
            Click(window.FindControl<Button>("CancelSaleButton")!);
        }
        finally
        {
            repository.AllowFirstLookup.TrySetResult();
            repository.AllowSecondLookup.TrySetResult();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SavingSaleDisablesScannerAndCartMutationsUntilCommitFinishes()
    {
        var product = new Product
        {
            Name = "저장 대기 상품",
            Barcode = "PENDING-001",
            Price = 1_000,
            Stock = 3,
        };
        var repository = new PendingSaleRepository(product);
        var window = new SalesWindow(repository, OutOfStockPolicy.Block);
        await window.InitializeAsync();
        window.Show();

        try
        {
            await ScanAsync(window, product.Barcode);
            Click(window.FindControl<Button>("CompleteSaleButton")!);
            Click(window.FindControl<Button>("ConfirmSaleButton")!);
            await WaitForAsync(() => repository.SaveStarted.Task.IsCompleted);

            Assert.False(window.FindControl<TextBox>("BarcodeInput")!.IsEnabled);
            Assert.False(window.FindControl<Button>("ClearCartButton")!.IsEnabled);
            var increase = FindCartButton(window, "+");
            Assert.False(increase.IsEnabled);
            Click(increase);
            Assert.Contains('1', window.FindControl<TextBlock>("TotalItemCountText")!.Text!);

            repository.AllowSave.TrySetResult();
            await WaitForAsync(() => window.FindControl<TextBlock>("TotalItemCountText")!.Text!.Contains('0'));
            Assert.True(window.FindControl<TextBox>("BarcodeInput")!.IsEnabled);
        }
        finally
        {
            repository.AllowSave.TrySetResult();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CommittedSaleRemainsSuccessfulWhenProductReloadFails()
    {
        var product = new Product
        {
            Name = "새로고침 실패 상품",
            Barcode = "RELOAD-FAIL-001",
            Price = 3_000,
            Stock = 2,
        };
        var repository = new ReloadFailingRepository(product);
        var window = new SalesWindow(repository, OutOfStockPolicy.Block);
        await window.InitializeAsync();
        window.Show();

        try
        {
            await ScanAsync(window, product.Barcode);
            Click(window.FindControl<Button>("CompleteSaleButton")!);
            Click(window.FindControl<Button>("ConfirmSaleButton")!);
            await WaitForAsync(() => window.FindControl<TextBlock>("StatusMessage")!.Text!.Contains("새로고침"));

            Assert.True(repository.SaleCommitted);
            Assert.Contains('0', window.FindControl<TextBlock>("TotalItemCountText")!.Text!);
            Assert.Contains("저장되었습니다", window.FindControl<TextBlock>("StatusMessage")!.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task FailedSaleKeepsCartAndShowsError()
    {
        var product = new Product
        {
            Name = "저장 실패 상품",
            Barcode = "FAIL-001",
            Price = 2_000,
            Stock = 2,
        };
        var repository = new FailingSaleRepository(product);
        var window = new SalesWindow(repository, OutOfStockPolicy.Block);
        await window.InitializeAsync();
        window.Show();

        try
        {
            var barcodeInput = window.FindControl<TextBox>("BarcodeInput")!;
            barcodeInput.Text = product.Barcode;
            barcodeInput.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                Source = barcodeInput,
            });
            await WaitForAsync(() => window.FindControl<TextBlock>("TotalItemCountText")!.Text!.Contains('1'));
            Click(window.FindControl<Button>("CompleteSaleButton")!);
            await WaitForAsync(() => window.FindControl<Control>("SaleConfirmationPanel")!.IsVisible);
            Click(window.FindControl<Button>("ConfirmSaleButton")!);
            await WaitForAsync(() => window.FindControl<TextBlock>("StatusMessage")!.Text!.Contains("저장"));

            Assert.Contains('1', window.FindControl<TextBlock>("TotalItemCountText")!.Text!);
            Assert.Contains("저장", window.FindControl<TextBlock>("StatusMessage")!.Text);
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

    private async Task<string> CreateDatabaseAsync()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, "판매 DB.xlsx");
        await new ExcelPosRepository(path).CreateAsync();
        return path;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected UI state was not reached.");
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task ScanAsync(SalesWindow window, string barcode)
    {
        RaiseScan(window, barcode);
        await WaitForAsync(() => window.FindControl<TextBlock>("TotalItemCountText")!.Text!.Contains('1'));
    }

    private static void RaiseScan(SalesWindow window, string barcode)
    {
        var barcodeInput = window.FindControl<TextBox>("BarcodeInput")!;
        barcodeInput.Text = barcode;
        barcodeInput.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
            Source = barcodeInput,
        });
    }

    private static Button FindCartButton(SalesWindow window, string content)
    {
        var cart = window.FindControl<StackPanel>("CartItemsPanel")!;
        return cart.Children
            .OfType<Border>()
            .Select(border => border.Child)
            .OfType<Grid>()
            .SelectMany(grid => grid.Children)
            .OfType<Button>()
            .Single(button => string.Equals(button.Content as string, content, StringComparison.Ordinal));
    }

    private sealed class FailingSaleRepository(Product product) : IPosRepository
    {
        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Product>>([product]);

        public Task<Product?> FindProductByBarcodeAsync(
            string barcode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Product?>(string.Equals(product.Barcode, barcode, StringComparison.Ordinal) ? product : null);

        public Task SaveProductAsync(Product product, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveSaleAsync(
            Sale sale,
            IReadOnlyCollection<Product> inventoryUpdates,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("판매 저장 실패"));
    }

    private sealed class PendingSaleRepository(Product product) : IPosRepository
    {
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Product>>([product]);

        public Task<Product?> FindProductByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult<Product?>(string.Equals(product.Barcode, barcode, StringComparison.Ordinal) ? product : null);

        public Task SaveProductAsync(Product product, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task SaveSaleAsync(
            Sale sale,
            IReadOnlyCollection<Product> inventoryUpdates,
            CancellationToken cancellationToken = default)
        {
            SaveStarted.TrySetResult();
            await AllowSave.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ReloadFailingRepository(Product product) : IPosRepository
    {
        private int _loadCount;
        public bool SaleCommitted { get; private set; }

        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _loadCount) > 1)
            {
                throw new IOException("상품 새로고침 실패");
            }

            return Task.FromResult<IReadOnlyList<Product>>([product]);
        }

        public Task<Product?> FindProductByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult<Product?>(string.Equals(product.Barcode, barcode, StringComparison.Ordinal) ? product : null);

        public Task SaveProductAsync(Product product, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveSaleAsync(
            Sale sale,
            IReadOnlyCollection<Product> inventoryUpdates,
            CancellationToken cancellationToken = default)
        {
            SaleCommitted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class DelayedLookupRepository(Product product) : IPosRepository
    {
        private int _lookupCount;
        public TaskCompletionSource FirstLookupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondLookupStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowFirstLookup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowSecondLookup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Product>>([product]);

        public async Task<Product?> FindProductByBarcodeAsync(
            string barcode,
            CancellationToken cancellationToken = default)
        {
            var lookup = Interlocked.Increment(ref _lookupCount);
            var started = lookup == 1 ? FirstLookupStarted : SecondLookupStarted;
            var allowed = lookup == 1 ? AllowFirstLookup : AllowSecondLookup;
            started.TrySetResult();
            await allowed.Task.WaitAsync(cancellationToken);
            return string.Equals(product.Barcode, barcode, StringComparison.Ordinal) ? product : null;
        }

        public Task SaveProductAsync(Product product, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveSaleAsync(
            Sale sale,
            IReadOnlyCollection<Product> inventoryUpdates,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
