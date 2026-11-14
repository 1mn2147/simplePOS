using System.IO;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pos.App.Views;
using Pos.App.Views.Controls;
using Pos.App.Services;
using Pos.App.ViewModels;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.App.Tests;

public sealed partial class PosWorkflowTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ApplyingSettingsForSameDatabasePreservesCartAndUndo(bool save) => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var shell = fixture.Shell;
        Assert.True(shell.MainPage!.AddProduct(fixture.Product, 2));
        shell.MainPage.ClearCartCommand.Execute(null);
        shell.NavigateSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        await ((AsyncRelayCommand)(save ? settings.SaveSettingsCommand : settings.ReloadDatabaseCommand)).ExecuteAsync();
        Assert.True(shell.MainPage.CanUndoCart);
        shell.MainPage.UndoCartCommand.Execute(null);
        Assert.Equal(2, shell.MainPage.TotalItemCount);
        Assert.Equal(3000, shell.MainPage.TotalAmount);

        shell.NavigateSettingsCommand.Execute(null);
        settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        await ((AsyncRelayCommand)(save ? settings.SaveSettingsCommand : settings.ReloadDatabaseCommand)).ExecuteAsync();
        Assert.Equal(2, shell.MainPage.TotalItemCount);
    });

    [Fact]
    public Task DatabaseSwitchDoesNotDiscardAnActiveCartOrWriteSettings() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var shell = fixture.Shell;
        shell.MainPage!.AddProduct(fixture.Product);
        var originalPath = shell.Settings.DatabasePath;
        var before = await File.ReadAllTextAsync(fixture.Paths.SettingsPath);
        shell.NavigateSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        var replacement = Path.Combine(fixture.Root, "other.xlsx");
        settings.Load(shell.Settings with { DatabasePath = replacement }, fixture.Paths.DefaultDatabasePath, Path.Combine(fixture.Root, "backups"));
        await ((AsyncRelayCommand)settings.SaveSettingsCommand).ExecuteAsync();

        Assert.Same(settings, shell.CurrentPage);
        Assert.Equal(originalPath, shell.Settings.DatabasePath);
        Assert.Equal(1, shell.MainPage.TotalItemCount);
        Assert.Equal(before, await File.ReadAllTextAsync(fixture.Paths.SettingsPath));
        Assert.False(File.Exists(replacement));
        Assert.Contains("장바구니", Assert.Single(fixture.Dialogs.Information));
    });

    [Fact]
    public Task SearchCanAddANonQuickProductWithQuantityAndReturnToSale() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var shell = fixture.Shell;
        await ((AsyncRelayCommand)shell.NavigateProductListCommand).ExecuteAsync();
        var list = Assert.IsType<ProductListViewModel>(shell.CurrentPage);
        list.SearchText = "테스트 우유";
        list.SelectedProduct = Assert.Single(list.Products);
        list.QuantityText = "3";
        await ((AsyncRelayCommand)list.AddToCartCommand).ExecuteAsync();

        Assert.Same(shell.MainPage, shell.CurrentPage);
        Assert.Equal(3, shell.MainPage!.TotalItemCount);
        Assert.Equal(4500, shell.MainPage.TotalAmount);
    });

    [Fact]
    public Task InvalidQuantityAndStockRejectionKeepSelectionAndCart() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var shell = fixture.Shell;
        await ((AsyncRelayCommand)shell.NavigateProductListCommand).ExecuteAsync();
        var list = Assert.IsType<ProductListViewModel>(shell.CurrentPage);
        list.SelectedProduct = Assert.Single(list.Products);
        list.QuantityText = "0";
        await ((AsyncRelayCommand)list.AddToCartCommand).ExecuteAsync();
        Assert.Contains("수량", list.SelectionStatusText);
        Assert.Equal(0, shell.MainPage!.TotalItemCount);
        list.QuantityText = "21";
        await ((AsyncRelayCommand)list.AddToCartCommand).ExecuteAsync();
        Assert.Same(list, shell.CurrentPage);
        Assert.Equal(0, shell.MainPage.TotalItemCount);
        list.SearchText = "없는 상품";
        Assert.False(list.AddToCartCommand.CanExecute(null));
    });

    [Fact]
    public Task UnsavedProductNavigationCanBeCancelled() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var shell = fixture.Shell;
        await ((AsyncRelayCommand)shell.NavigateProductEditorCommand).ExecuteAsync();
        var editor = Assert.IsType<ProductEditorViewModel>(shell.CurrentPage);
        editor.ProductName = "작성 중";
        fixture.Dialogs.ConfirmResult = false;
        await ((AsyncRelayCommand)shell.NavigateMainCommand).ExecuteAsync();
        Assert.Same(editor, shell.CurrentPage);
        Assert.Equal("작성 중", editor.ProductName);
        fixture.Dialogs.ConfirmResult = true;
        await ((AsyncRelayCommand)shell.NavigateMainCommand).ExecuteAsync();
        Assert.Same(shell.MainPage, shell.CurrentPage);
    });

    [Fact]
    public Task VirtualInputKeepsPhysicalEditsAndDismissesOnNavigation() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var shell = fixture.Shell;
        await ((AsyncRelayCommand)shell.NavigateProductListCommand).ExecuteAsync();
        var list = Assert.IsType<ProductListViewModel>(shell.CurrentPage);
        Assert.False(list.IsInputPanelVisible);
        list.ShowKoreanKeyboardCommand.Execute("Search");
        list.KoreanKeyCommand.Execute("ㄱ");
        list.KoreanKeyCommand.Execute("ㅏ");
        Assert.Equal("가", list.ActiveInputValue);
        list.ActiveInputValue = "우유";
        list.KoreanKeyCommand.Execute("ㄱ");
        list.KoreanKeyCommand.Execute("ㅏ");
        Assert.Equal("우유가", list.SearchText);
        await ((AsyncRelayCommand)shell.NavigateMainCommand).ExecuteAsync();
        Assert.False(list.IsInputPanelVisible);
    });

    [Theory]
    [InlineData(OutOfStockPolicy.Block, false, "품절")]
    [InlineData(OutOfStockPolicy.WarnAndAllow, true, "재고 0 · 확인 후 판매")]
    [InlineData(OutOfStockPolicy.Allow, true, "재고 0 · 판매 허용")]
    public void SoldOutTilesExplainTheActivePolicy(OutOfStockPolicy policy, bool canAdd, string status)
    {
        var item = new QuickAddItemViewModel(new Product { Name = "품절 상품", Stock = 0 }, policy);
        Assert.Equal(canAdd, item.CanAdd);
        Assert.Equal(status, item.StockStatusText);
    }

    [Fact]
    public Task MinimumWindowKeepsCartRowsAndCheckoutVisible() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        for (var index = 0; index < 3; index++) fixture.Shell.MainPage!.AddProduct(fixture.Product with { Id = Guid.NewGuid(), Name = $"상품 {index}" });
        var page = new MainPage { DataContext = fixture.Shell.MainPage };
        var window = new Window { Width = 900, Height = 640, Left = -20000, Top = -20000, ShowInTaskbar = false, Content = page, DataContext = fixture.Shell };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var cart = Descendants<ListView>(page).Single();
            Assert.True(cart.ActualHeight >= 220, $"Cart height was {cart.ActualHeight}");
            var checkout = Descendants<Button>(page).Single(button => button.IsVisible && button.Content as string == "판매 완료");
            Assert.True(checkout.TranslatePoint(new Point(), page).Y + checkout.ActualHeight <= page.ActualHeight);
            Assert.Equal(2, fixture.Shell.MainPage!.QuickAddColumns);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DockedKeyboardLeavesResultsAndNavigationReachable() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        await ((AsyncRelayCommand)fixture.Shell.NavigateProductListCommand).ExecuteAsync();
        var list = Assert.IsType<ProductListViewModel>(fixture.Shell.CurrentPage);
        var page = new ProductListPage { DataContext = list };
        var window = new Window { Width = 900, Height = 640, Left = -20000, Top = -20000, ShowInTaskbar = false, Content = page, DataContext = fixture.Shell };
        try
        {
            window.Show();
            list.ShowKoreanKeyboardCommand.Execute("Search");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = Descendants<DataGrid>(page).Single();
            Assert.True(grid.ActualHeight >= 100, $"Results height was {grid.ActualHeight}");
            var keyboard = Descendants<KoreanKeyboard>(page).Single();
            var navigation = Descendants<BottomNavigation>(page).Single();
            Assert.True(keyboard.TranslatePoint(new Point(), page).Y + keyboard.ActualHeight <= navigation.TranslatePoint(new Point(), page).Y);
            var input = Descendants<TextBox>(page).Single(box => box.Tag as string == "ActiveInput" && box.IsVisible);
            input.Text = "테스트 우유";
            Assert.Equal("테스트 우유", list.SearchText);
            list.ConfirmInputCommand.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(keyboard.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SavingSaleRejectsNewCartMutationsUntilItFinishes() => Run(async () =>
    {
        var repository = new PendingRepository();
        var cart = new CartService();
        var product = new Product { Name = "상품", Price = 1000 };
        using var main = new MainPageViewModel(repository, cart, new SaleService(repository), new RecordingDialogs(), OutOfStockPolicy.Block);
        main.AddProduct(product);
        var saving = main.CompleteSaleCommand.ExecuteAsync();
        Assert.True(main.IsSavingSale);
        Assert.False(main.AddProduct(product));
        Assert.False(main.ClearCartCommand.CanExecute(null));
        repository.Saved.SetResult();
        await saving;
        Assert.Equal(0, main.TotalItemCount);
        Assert.False(main.CanUndoCart);
        Assert.False(main.IsSavingSale);
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class PendingRepository : IPosRepository
    {
        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>([]);
        public Task<Product?> FindProductByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) => Task.FromResult<Product?>(null);
        public Task SaveProductAsync(Product product, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveSaleAsync(Sale sale, IReadOnlyCollection<Product> inventoryUpdates, CancellationToken cancellationToken = default) => Saved.Task;
    }

    private static Task Run(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception exception) { completion.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "SimplePOS-UI-tests", Guid.NewGuid().ToString("N"));
        public AppPaths Paths { get; }
        public RecordingDialogs Dialogs { get; } = new();
        public ShellViewModel Shell { get; }
        public Product Product { get; } = new() { Name = "테스트 우유", Price = 1500, Stock = 20, QuickAdd = false };

        public Fixture()
        {
            Paths = new AppPaths(Root);
            Shell = new ShellViewModel(Paths, new JsonSettingsService(), new BackupService(), new StartupRegistrationService(startupDirectory: Path.Combine(Root, "startup")), Dialogs);
        }

        public async Task InitializeAsync()
        {
            await Shell.InitializeAsync();
            await new ExcelPosRepository(Paths.DefaultDatabasePath).SaveProductAsync(Product);
            await Shell.MainPage!.LoadAsync();
        }

        public void Dispose()
        {
            Shell.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;
        public int ConfirmCount { get; private set; }
        public bool ThrowOnError { get; set; } = true;
        public List<string> Errors { get; } = [];
        public List<string> Information { get; } = [];
        public bool Confirm(string message, string title) { ConfirmCount++; return ConfirmResult; }
        public void Error(string message, string title = "오류")
        {
            Errors.Add(message);
            if (ThrowOnError) throw new InvalidOperationException(message);
        }
        public void Info(string message, string title = "Simple POS") => Information.Add(message);
    }
}
