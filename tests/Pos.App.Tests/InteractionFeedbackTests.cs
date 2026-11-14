using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pos.App.ViewModels;
using Pos.App.Views;
using Pos.App.Views.Controls;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.App.Tests;

public sealed partial class PosWorkflowTests
{
    [Fact]
    public Task RepeatSalesUseOneConfirmationAndANonBlockingSuccessNotice() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var main = fixture.Shell.MainPage!;
        main.AddProduct(fixture.Product, 2);
        await main.CompleteSaleCommand.ExecuteAsync();
        Assert.Equal(1, fixture.Dialogs.ConfirmCount);
        Assert.Empty(fixture.Dialogs.Information);
        Assert.True(main.HasSaleNotice);
        Assert.Contains("3,000원", main.SaleNoticeText);
        Assert.Contains("2개", main.SaleNoticeText);
        Assert.Equal(0, main.TotalItemCount);

        var page = new MainPage { DataContext = main };
        var window = TestWindow(page, fixture.Shell);
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Contains(Descendants<TextBlock>(page), text => text.IsVisible && text.Text == main.SaleNoticeText);
            CaptureQa(window, "sale-success-900");
        }
        finally { window.Close(); }

        Assert.True(main.AddProduct(fixture.Product));
        Assert.False(main.HasSaleNotice);
        await main.CompleteSaleCommand.ExecuteAsync();
        Assert.Equal(2, fixture.Dialogs.ConfirmCount);
        Assert.Empty(fixture.Dialogs.Information);
        Assert.Contains("1,500원", main.SaleNoticeText);
        Assert.Equal(17, Assert.Single(await new ExcelPosRepository(fixture.Paths.DefaultDatabasePath).GetProductsAsync()).Stock);
        main.DismissSaleNoticeCommand.Execute(null);
        Assert.False(main.HasSaleNotice);
    });

    [Fact]
    public Task CancelledSaleKeepsCartWithoutASuccessNotice() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        fixture.Dialogs.ConfirmResult = false;
        var main = fixture.Shell.MainPage!;
        main.AddProduct(fixture.Product);
        await main.CompleteSaleCommand.ExecuteAsync();
        Assert.Equal(1, main.TotalItemCount);
        Assert.False(main.HasSaleNotice);
        Assert.Empty(fixture.Dialogs.Information);
    });

    [Fact]
    public Task FailedSaleDoesNotShowSuccessOrLoseCart() => Run(async () =>
    {
        var repository = new PendingRepository();
        var dialogs = new RecordingDialogs { ThrowOnError = false };
        using var main = new MainPageViewModel(repository, new CartService(), new SaleService(repository), dialogs, OutOfStockPolicy.Block);
        main.AddProduct(new Product { Name = "상품", Price = 1000 });
        var saving = main.CompleteSaleCommand.ExecuteAsync();
        repository.Saved.SetException(new IOException("파일 잠김"));
        await saving;
        Assert.Equal(1, main.TotalItemCount);
        Assert.False(main.HasSaleNotice);
        Assert.False(main.IsSavingSale);
        Assert.Single(dialogs.Errors);
        Assert.Empty(dialogs.Information);
    });

    [Fact]
    public Task ProductErrorsHighlightFieldsFocusFirstAndRefreshWhileEditing() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        await new ExcelPosRepository(fixture.Paths.DefaultDatabasePath).SaveProductAsync(fixture.Product with { Barcode = "880123" });
        await ((AsyncRelayCommand)fixture.Shell.NavigateProductEditorCommand).ExecuteAsync();
        var editor = Assert.IsType<ProductEditorViewModel>(fixture.Shell.CurrentPage);
        editor.PriceText = "가격";
        editor.StockText = "-1";
        editor.Barcode = "880123";
        var focus = new List<string>();
        editor.FocusFieldRequested += (_, inputName) => focus.Add(inputName);
        var page = new ProductEditorPage { DataContext = editor };
        var window = TestWindow(page, fixture.Shell);
        try
        {
            window.Show();
            await ((AsyncRelayCommand)editor.SaveProductCommand).ExecuteAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(4, editor.ErrorCount);
            Assert.Equal(nameof(editor.ProductName), Assert.Single(focus));
            var name = Descendants<TextBox>(page).Single(box => box.Tag as string == "ProductName");
            var price = Descendants<TextBox>(page).Single(box => box.Tag as string == "Price");
            Assert.True(Validation.GetHasError(name));
            Assert.True(Validation.GetHasError(price));
            Assert.Equal(Color.FromRgb(185, 28, 28), Assert.IsType<SolidColorBrush>(name.BorderBrush).Color);
            Assert.Same(name, FocusManager.GetFocusedElement(window));
            CaptureQa(window, "product-errors-900");

            editor.ProductName = "새 상품";
            editor.PriceText = "2000";
            editor.StockText = "10";
            editor.Barcode = "880456";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(editor.HasErrors);
            Assert.Empty(editor.ProductNameError);
            Assert.Empty(editor.PriceError);
            Assert.False(Validation.GetHasError(name));
            Assert.False(Validation.GetHasError(price));
            Assert.Empty(fixture.Dialogs.Errors);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task InvalidSettingsAreShownInlineAndCanBeCorrectedWithoutDialogs() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        fixture.Shell.NavigateSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(fixture.Shell.CurrentPage);
        var before = await File.ReadAllTextAsync(fixture.Paths.SettingsPath);
        settings.BackupTimeText = "25:99";
        settings.BackupRetentionCountText = "0";
        settings.ScannerInputThresholdText = "오류";
        var focus = new List<string>();
        settings.FocusFieldRequested += (_, inputName) => focus.Add(inputName);
        var page = new SettingsPage { DataContext = settings };
        var window = TestWindow(page, fixture.Shell);
        try
        {
            window.Show();
            await ((AsyncRelayCommand)settings.SaveSettingsCommand).ExecuteAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(3, settings.ErrorCount);
            Assert.Equal(nameof(settings.BackupTimeText), Assert.Single(focus));
            var time = Descendants<TextBox>(page).Single(box => box.Tag as string == nameof(settings.BackupTimeText));
            Assert.True(Validation.GetHasError(time));
            Assert.Same(time, FocusManager.GetFocusedElement(window));
            Assert.Empty(fixture.Dialogs.Errors);
            Assert.Equal(before, await File.ReadAllTextAsync(fixture.Paths.SettingsPath));
            CaptureQa(window, "settings-errors-900");
            settings.BackupTimeText = "03:00";
            settings.BackupRetentionCountText = "30";
            settings.ScannerInputThresholdText = "80";
            Assert.False(settings.HasErrors);
            Assert.Empty(settings.BackupTimeError);
            Assert.Empty(settings.BackupRetentionError);
            Assert.Empty(settings.ScannerInputError);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CategoryKeyboardShowsShiftedKeysAndSavesComposedText() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        await ((AsyncRelayCommand)fixture.Shell.NavigateProductEditorCommand).ExecuteAsync();
        var editor = Assert.IsType<ProductEditorViewModel>(fixture.Shell.CurrentPage);
        editor.ProductName = "새 상품";
        editor.PriceText = "1000";
        var page = new ProductEditorPage { DataContext = editor };
        var window = TestWindow(page, fixture.Shell);
        try
        {
            window.Show();
            var categoryButton = Descendants<Button>(page).Single(button => button.CommandParameter as string == "Category");
            categoryButton.Command.Execute(categoryButton.CommandParameter);
            editor.ToggleKoreanShiftCommand.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var keyboard = Descendants<KoreanKeyboard>(page).Single();
            Assert.True(keyboard.IsVisible);
            Assert.True(editor.IsKoreanShift);
            Assert.Equal("카테고리", editor.ActiveInputLabel);
            foreach (var pair in new Dictionary<string, string> { ["ㅂ"] = "ㅃ", ["ㅈ"] = "ㅉ", ["ㄷ"] = "ㄸ", ["ㄱ"] = "ㄲ", ["ㅅ"] = "ㅆ", ["ㅐ"] = "ㅒ", ["ㅔ"] = "ㅖ" })
                Assert.Equal(pair.Value, Descendants<Button>(keyboard).Single(button => button.CommandParameter as string == pair.Key).Content);
            CaptureQa(window, "category-shift-900");
            editor.KoreanKeyCommand.Execute("ㄱ");
            editor.KoreanKeyCommand.Execute("ㅏ");
            Assert.Equal("까", editor.CategoryText);
            Assert.False(editor.IsKoreanShift);
            Assert.Equal("ㄱ", editor.KoreanKeyLabels["ㄱ"]);
            editor.ToggleKoreanShiftCommand.Execute(null);
            editor.KoreanKeyCommand.Execute(" ");
            Assert.False(editor.IsKoreanShift);
            editor.ConfirmInputCommand.Execute(null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(keyboard.IsVisible);
            var category = Descendants<ComboBox>(page).Single(combo => combo.Tag as string == "Category");
            Assert.True(category.IsKeyboardFocusWithin || ReferenceEquals(FocusManager.GetFocusedElement(window), category));
            await ((AsyncRelayCommand)editor.SaveProductCommand).ExecuteAsync();
            var saved = (await new ExcelPosRepository(fixture.Paths.DefaultDatabasePath).GetProductsAsync()).Single(product => product.Name == "새 상품");
            Assert.Equal("까", saved.Category);
        }
        finally { window.Close(); }
    });

    private static Window TestWindow(FrameworkElement page, object dataContext) => new()
    {
        Width = 900, Height = 640, Left = -20000, Top = -20000,
        ShowInTaskbar = false, Content = page, DataContext = dataContext,
    };

    private static void CaptureQa(Window window, string name)
    {
        var output = Environment.GetEnvironmentVariable("SIMPLEPOS_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(stream);
    }
}
