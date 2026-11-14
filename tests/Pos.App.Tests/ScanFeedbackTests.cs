using System.IO;
using Pos.App.Services;
using Pos.App.ViewModels;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.App.Tests;

public sealed partial class PosWorkflowTests
{
    [Theory]
    [InlineData(OutOfStockPolicy.Block, true, BarcodeHandlingResult.Blocked)]
    [InlineData(OutOfStockPolicy.WarnAndAllow, false, BarcodeHandlingResult.Cancelled)]
    [InlineData(OutOfStockPolicy.WarnAndAllow, true, BarcodeHandlingResult.Added)]
    public Task ScanFeedbackDistinguishesStockRejectionCancellationAndAddition(
        OutOfStockPolicy policy, bool confirm, BarcodeHandlingResult expected) => Run(async () =>
    {
        var product = new Product { Name = "품절 우유", Price = 1500, Stock = 0, Barcode = "880001" };
        var repository = new FeedbackRepository(product);
        var dialogs = new RecordingDialogs { ConfirmResult = confirm };
        using var main = new MainPageViewModel(repository, new CartService(), new SaleService(repository), dialogs, policy);

        Assert.Equal(expected, await main.HandleBarcodeAsync("880001"));
        Assert.Equal(expected == BarcodeHandlingResult.Added ? 1 : 0, main.TotalItemCount);
        Assert.True(main.HasActivityFeedback);
        Assert.Contains(product.Name, main.ActivityFeedbackText);
        Assert.Equal(expected == BarcodeHandlingResult.Added, main.ActivityFeedbackKind == "Success");
        if (expected == BarcodeHandlingResult.Cancelled) Assert.Contains("취소", main.ActivityFeedbackText);
    });

    [Fact]
    public Task RepeatedScanHighlightsTheLatestProductAndItsActualCartQuantity() => Run(async () =>
    {
        var milk = new Product { Name = "테스트 우유", Price = 1500, Stock = 20, Barcode = "880001" };
        var water = new Product { Name = "생수", Price = 1000, Stock = 20, Barcode = "880002" };
        var repository = new FeedbackRepository(milk, water);
        using var main = new MainPageViewModel(repository, new CartService(), new SaleService(repository), new RecordingDialogs(), OutOfStockPolicy.Block);

        await main.HandleBarcodeAsync(milk.Barcode!);
        await main.HandleBarcodeAsync(milk.Barcode!);
        Assert.Equal(2, main.RecentProductQuantity);
        Assert.Contains("현재 2개", main.ActivityFeedbackText);
        Assert.True(Assert.Single(main.CartItems).IsRecentlyAdded);

        await main.HandleBarcodeAsync(water.Barcode!);
        Assert.Equal(water.Id, main.RecentProductId);
        Assert.Equal(water.Id, Assert.Single(main.CartItems, item => item.IsRecentlyAdded).ProductId);
        main.DismissActivityFeedbackCommand.Execute(null);
        Assert.False(main.HasActivityFeedback);
        Assert.Null(main.RecentProductId);
        Assert.DoesNotContain(main.CartItems, item => item.IsRecentlyAdded);
        Assert.Equal(3, main.TotalItemCount);
    });

    [Fact]
    public Task FailedScanKeepsTheCartAndReplacesEarlierSuccessFeedback() => Run(async () =>
    {
        var milk = new Product { Name = "테스트 우유", Price = 1500, Barcode = "880001" };
        var repository = new FeedbackRepository(milk);
        var dialogs = new RecordingDialogs { ThrowOnError = false };
        using var main = new MainPageViewModel(repository, new CartService(), new SaleService(repository), dialogs, OutOfStockPolicy.Block);
        await main.HandleBarcodeAsync(milk.Barcode!);
        repository.FailLookup = true;

        Assert.Equal(BarcodeHandlingResult.Failed, await main.HandleBarcodeAsync(milk.Barcode!));
        Assert.Equal(1, main.TotalItemCount);
        Assert.Equal("Error", main.ActivityFeedbackKind);
        Assert.Null(main.RecentProductId);
        Assert.Contains("다시 스캔", main.ActivityFeedbackText);
        Assert.Single(dialogs.Errors);
    });

    [Fact]
    public Task ScanDuringSaleSavingCannotOpenRegistrationAndItsWarningClearsOnSuccess() => Run(async () =>
    {
        var repository = new PendingRepository();
        using var main = new MainPageViewModel(repository, new CartService(), new SaleService(repository), new RecordingDialogs(), OutOfStockPolicy.Block);
        var registrations = 0;
        main.UnknownBarcodeRequested = _ => { registrations++; return Task.CompletedTask; };
        main.AddProduct(new Product { Name = "판매할 상품", Price = 1000 });
        var saving = main.CompleteSaleCommand.ExecuteAsync();

        Assert.Equal(BarcodeHandlingResult.Blocked, await main.HandleBarcodeAsync("880999"));
        Assert.Equal(0, registrations);
        Assert.Contains("저장 중", main.ActivityFeedbackText);
        repository.Saved.SetResult();
        await saving;
        Assert.False(main.HasActivityFeedback);
        Assert.True(main.HasSaleNotice);
    });

    [Theory]
    [InlineData(OutOfStockPolicy.Block)]
    [InlineData(OutOfStockPolicy.WarnAndAllow)]
    public Task ScannerSuccessSoundOnlyPlaysAfterAnActualAddition(OutOfStockPolicy policy) => Run(async () =>
    {
        using var fixture = new Fixture();
        var settings = new JsonSettingsService();
        await settings.SaveAsync(fixture.Paths.SettingsPath, new AppSettings
        {
            DatabasePath = fixture.Paths.DefaultDatabasePath,
            BackupDirectory = Path.Combine(fixture.Root, "backups"),
            OutOfStockPolicy = policy,
            ScannerSuccessSoundEnabled = true,
        });
        var sounds = 0;
        using var shell = new ShellViewModel(fixture.Paths, settings, new BackupService(),
            new StartupRegistrationService(startupDirectory: Path.Combine(fixture.Root, "startup")), fixture.Dialogs, () => sounds++);
        await shell.InitializeAsync();
        var repository = new ExcelPosRepository(fixture.Paths.DefaultDatabasePath);
        var milk = fixture.Product with { Barcode = "880001" };
        await repository.SaveProductAsync(milk);
        await repository.SaveProductAsync(milk with { Id = Guid.NewGuid(), Name = "품절 상품", Barcode = "880002", Stock = 0 });
        await repository.SaveProductAsync(milk with { Id = Guid.NewGuid(), Name = "판매 중지 상품", Barcode = "880003", IsActive = false });

        await SendScanAsync(shell, "880001");
        Assert.Equal(1, sounds);
        fixture.Dialogs.ConfirmResult = false;
        await SendScanAsync(shell, "880002");
        await SendScanAsync(shell, "880003");
        Assert.Equal(1, sounds);
        Assert.Same(shell.MainPage, shell.CurrentPage);
        Assert.Contains("판매 중지", shell.MainPage!.ActivityFeedbackText);

        await SendScanAsync(shell, "880999");
        Assert.Equal(1, sounds);
        Assert.IsType<ProductEditorViewModel>(shell.CurrentPage);
        await ((AsyncRelayCommand)shell.NavigateMainCommand).ExecuteAsync();
        fixture.Dialogs.ThrowOnError = false;
        using (var lockedDatabase = new FileStream(fixture.Paths.DefaultDatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await SendScanAsync(shell, "880001");
            Assert.Equal(1, sounds);
            Assert.Equal(1, shell.MainPage.TotalItemCount);
            Assert.Equal("Error", shell.MainPage.ActivityFeedbackKind);
            Assert.Single(fixture.Dialogs.Errors);
        }

        await SendScanAsync(shell, "880001");
        Assert.Equal(2, sounds);
        Assert.Equal(2, shell.MainPage.TotalItemCount);
        Assert.Equal("Success", shell.MainPage.ActivityFeedbackKind);
    });

    [Fact]
    public Task UnknownBarcodeRegistrationReturnsWithNoticeWithoutAddingToTheCart() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        Assert.Equal(BarcodeHandlingResult.UnknownBarcode, await fixture.Shell.MainPage!.HandleBarcodeAsync("880999"));
        var editor = Assert.IsType<ProductEditorViewModel>(fixture.Shell.CurrentPage);
        Assert.Contains("미등록 바코드", editor.EditorFeedbackText);
        editor.ProductName = "새 바코드 상품";
        editor.PriceText = "2000";
        await ((AsyncRelayCommand)editor.SaveProductCommand).ExecuteAsync();

        Assert.Same(fixture.Shell.MainPage, fixture.Shell.CurrentPage);
        Assert.Equal(0, fixture.Shell.MainPage.TotalItemCount);
        Assert.Contains("등록 완료", fixture.Shell.MainPage.ActivityFeedbackText);
        Assert.Contains("다시 스캔", fixture.Shell.MainPage.ActivityFeedbackText);
        Assert.Equal("Success", fixture.Shell.MainPage.ActivityFeedbackKind);
        Assert.Equal(BarcodeHandlingResult.Added, await fixture.Shell.MainPage.HandleBarcodeAsync("880999"));
        Assert.Equal(1, fixture.Shell.MainPage.TotalItemCount);
    });

    [Fact]
    public Task ProductSaveFailureKeepsInputsAndDoesNotReportRegistrationSuccess() => Run(async () =>
    {
        var repository = new FeedbackRepository { FailSave = true };
        var dialogs = new RecordingDialogs { ThrowOnError = false };
        var editor = new ProductEditorViewModel(repository, new ProductValidator(), dialogs);
        var completions = 0;
        editor.Completed = () => { completions++; return Task.CompletedTask; };
        await editor.LoadAsync(barcode: "880999");
        editor.ProductName = "저장 중인 상품";
        editor.PriceText = "2000";
        await ((AsyncRelayCommand)editor.SaveProductCommand).ExecuteAsync();

        Assert.Equal("저장 중인 상품", editor.ProductName);
        Assert.Equal("2000", editor.PriceText);
        Assert.Equal("880999", editor.Barcode);
        Assert.Null(editor.SavedProduct);
        Assert.Empty(editor.SavedProductNotice);
        Assert.Equal(0, completions);
        Assert.True(editor.HasUnsavedChanges);
        Assert.Contains("저장 실패", editor.EditorFeedbackText);
    });

    private static async Task SendScanAsync(ShellViewModel shell, string barcode)
    {
        var time = DateTimeOffset.UtcNow;
        foreach (var character in barcode)
        {
            await shell.HandleScannerKeyAsync(character.ToString(), time, textInputActive: false);
            time = time.AddMilliseconds(10);
        }
        Assert.Equal(BarcodeInputStatus.Completed, await shell.HandleScannerKeyAsync("Enter", time, textInputActive: false));
    }

    private sealed class FeedbackRepository(params Product[] products) : IPosRepository
    {
        public bool FailLookup { get; set; }
        public bool FailSave { get; set; }
        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>(products);
        public Task<Product?> FindProductByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) => FailLookup
            ? Task.FromException<Product?>(new IOException("파일을 열 수 없습니다."))
            : Task.FromResult(products.FirstOrDefault(product => product.IsActive && product.Barcode == barcode));
        public Task SaveProductAsync(Product product, CancellationToken cancellationToken = default) => FailSave
            ? Task.FromException(new IOException("파일 잠김")) : Task.CompletedTask;
        public Task SaveSaleAsync(Sale sale, IReadOnlyCollection<Product> inventoryUpdates, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
