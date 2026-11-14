using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pos.App.ViewModels;
using Pos.App.Views;
using Pos.App.Views.Controls;
using Pos.Core;

namespace Pos.App.Tests;

public sealed partial class PosWorkflowTests
{
    [Fact]
    public Task EmptyCatalogOffersRegistrationWhileEmptySearchOffersRecovery() => Run(async () =>
    {
        var repository = new ManagementRepository();
        var list = new ProductListViewModel(repository);
        var registrationRequested = false;
        list.EditRequested = product => { registrationRequested = product is null; return Task.CompletedTask; };
        await list.LoadAsync();
        Assert.True(list.IsCatalogEmpty);
        Assert.Equal("첫 상품 추가", list.EmptyStateActionText);
        await ((AsyncRelayCommand)list.EmptyStateActionCommand).ExecuteAsync();
        Assert.True(registrationRequested);

        repository.Items.Add(new Product { Name = "우유", Price = 1500 });
        await list.LoadAsync();
        list.SearchText = "없는 상품";
        Assert.True(list.IsEmpty);
        Assert.False(list.IsCatalogEmpty);
        Assert.Equal("검색 조건 초기화", list.EmptyStateActionText);
        await ((AsyncRelayCommand)list.EmptyStateActionCommand).ExecuteAsync();
        Assert.Single(list.Products);
    });

    [Fact]
    public Task InactiveOnlyCatalogCanBeRecoveredAndExplainsDisabledCartAction() => Run(async () =>
    {
        var repository = new ManagementRepository();
        repository.Items.Add(new Product { Name = "판매 중지 상품", Price = 1000, IsActive = false });
        var list = new ProductListViewModel(repository);
        await list.LoadAsync();
        Assert.Equal("전체 상품 보기", list.EmptyStateActionText);
        await ((AsyncRelayCommand)list.EmptyStateActionCommand).ExecuteAsync();
        list.SelectedProduct = Assert.Single(list.Products);
        Assert.Contains("판매 중지", list.SelectionStatusText);
        Assert.False(list.AddToCartCommand.CanExecute(null));
    });

    [Fact]
    public Task EmptySearchRecoveryStaysAboveDockedKeyboardAtMinimumSize() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        await ((AsyncRelayCommand)fixture.Shell.NavigateProductListCommand).ExecuteAsync();
        var list = Assert.IsType<ProductListViewModel>(fixture.Shell.CurrentPage);
        list.SearchText = "없는 상품";
        var page = new ProductListPage { DataContext = list };
        var window = TestWindow(page, fixture.Shell);
        try
        {
            window.Show();
            list.ShowKoreanKeyboardCommand.Execute("Search");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var keyboard = Descendants<KoreanKeyboard>(page).Single();
            var recovery = Descendants<Button>(page).Single(button => ReferenceEquals(button.Command, list.EmptyStateActionCommand));
            var title = Descendants<TextBlock>(page).Single(block => block.Text == list.EmptyStateTitle);
            Assert.True(recovery.IsVisible);
            Assert.True(title.TranslatePoint(new Point(), page).Y >= 0);
            Assert.True(recovery.TranslatePoint(new Point(), page).Y + recovery.ActualHeight <= keyboard.TranslatePoint(new Point(), page).Y);
            CaptureQa(window, "products-empty-keyboard-900");
            await ((AsyncRelayCommand)list.EmptyStateActionCommand).ExecuteAsync();
            Assert.Single(list.Products);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DatabaseRefreshPreservesDraftAndDoesNotApplyUnrelatedSettings() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var shell = fixture.Shell;
        shell.NavigateSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        var original = shell.Settings;
        var storedSettings = await File.ReadAllTextAsync(fixture.Paths.SettingsPath);
        settings.ScannerInputThresholdText = "invalid draft";
        settings.IsScanSoundEnabled = !original.ScannerSuccessSoundEnabled;
        Assert.True(settings.HasUnsavedChanges);
        await ((AsyncRelayCommand)settings.ReloadDatabaseCommand).ExecuteAsync();
        Assert.Same(settings, shell.CurrentPage);
        Assert.Equal(original, shell.Settings);
        Assert.Equal("invalid draft", settings.ScannerInputThresholdText);
        Assert.True(settings.HasUnsavedChanges);
        Assert.Equal(storedSettings, await File.ReadAllTextAsync(fixture.Paths.SettingsPath));
        Assert.Contains("아직 적용되지 않았습니다", settings.SettingsStatusText);
        Assert.Empty(fixture.Dialogs.Errors);
    });

    [Fact]
    public Task UnsavedSettingsNavigationCanBeCancelledAndRevertedEditsAreClean() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var shell = fixture.Shell;
        shell.NavigateSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        var original = settings.BackupRetentionCountText;
        settings.BackupRetentionCountText = "12";
        fixture.Dialogs.ConfirmResult = false;
        await ((AsyncRelayCommand)shell.NavigateMainCommand).ExecuteAsync();
        Assert.Same(settings, shell.CurrentPage);
        Assert.Equal("12", settings.BackupRetentionCountText);
        settings.BackupRetentionCountText = original;
        Assert.False(settings.HasUnsavedChanges);
        await ((AsyncRelayCommand)shell.NavigateMainCommand).ExecuteAsync();
        Assert.Same(shell.MainPage, shell.CurrentPage);
        Assert.Equal(1, fixture.Dialogs.ConfirmCount);
    });

    private sealed class ManagementRepository : IPosRepository
    {
        public List<Product> Items { get; } = [];
        public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>(Items);
        public Task<Product?> FindProductByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) => Task.FromResult<Product?>(Items.FirstOrDefault(product => product.Barcode == barcode));
        public Task SaveProductAsync(Product product, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveSaleAsync(Sale sale, IReadOnlyCollection<Product> inventoryUpdates, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
