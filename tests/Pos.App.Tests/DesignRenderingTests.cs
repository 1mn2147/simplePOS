using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pos.App.ViewModels;
using Pos.App.Views;
using Pos.Core;

namespace Pos.App.Tests;

public sealed partial class PosWorkflowTests
{
    [Theory]
    [InlineData(900, 640)]
    [InlineData(1050, 768)]
    [InlineData(1280, 800)]
    public Task LargeAmountsAndLongProductNamesKeepCartActionsReachable(int width, int height) => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var product = fixture.Product with
        {
            Name = "국내산 유기농 무가당 플레인 요거트 대용량 선물 세트",
            Price = 999_999_999,
            Stock = null,
        };
        var main = fixture.Shell.MainPage!;
        Assert.True(main.AddProduct(product, CartService.MaximumQuantity));
        var page = new MainPage { DataContext = main };
        var window = TestWindow(page, fixture.Shell);
        window.Width = width;
        window.Height = height;
        window.FontFamily = (FontFamily)page.FindResource("FontBody");
        window.FontSize = (double)page.FindResource("FontSizeBody");
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var cart = Descendants<ListView>(page).Single();
            var row = (ListViewItem)cart.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.NotNull(row);
            var name = Descendants<TextBlock>(row).Single(text => text.Text == product.Name);
            Assert.True(name.ActualWidth >= 160, $"Product name width was {name.ActualWidth} at {width}");
            foreach (var text in Descendants<TextBlock>(row).Where(text => text.Text.EndsWith("원", StringComparison.Ordinal)))
            {
                var measured = new FormattedText(text.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
                    text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
                Assert.True(measured.WidthIncludingTrailingWhitespace <= text.ActualWidth + 1,
                    $"Amount '{text.Text}' needs {measured.WidthIncludingTrailingWhitespace}, has {text.ActualWidth}");
            }
            foreach (var button in Descendants<Button>(row))
            {
                Assert.True(button.ActualWidth >= 48 && button.ActualHeight >= 48);
                var position = button.TranslatePoint(new Point(), cart);
                Assert.True(position.X >= 0 && position.X + button.ActualWidth <= cart.ActualWidth);
            }
            var checkout = Descendants<Button>(page).Single(button => button.IsVisible && button.Content as string == "판매 완료");
            Assert.True(checkout.TranslatePoint(new Point(), page).Y + checkout.ActualHeight <= page.ActualHeight);
            CaptureQa(window, $"main-long-{width}");
            if (width == 900)
            {
                CaptureScaledRender(window, "main-long-900-125pct", 1.25);
                CaptureScaledRender(window, "main-long-900-150pct", 1.5);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ANewScanRevealsItsRowAfterTheCartHasScrolled() => Run(async () =>
    {
        var repository = new ManagementRepository();
        var products = Enumerable.Range(1, 15).Select(index => new Product
        {
            Name = $"상품 {index:00}", Price = index * 1000, Barcode = $"880{index:0000}",
        }).ToArray();
        repository.Items.AddRange(products);
        using var main = new MainPageViewModel(repository, new CartService(), new SaleService(repository),
            new RecordingDialogs(), OutOfStockPolicy.Block);
        var page = new MainPage { DataContext = main };
        var window = TestWindow(page, main);
        try
        {
            window.Show();
            foreach (var product in products) await main.HandleBarcodeAsync(product.Barcode!);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var list = Descendants<ListView>(page).Single();
            var latest = Assert.Single(main.CartItems, item => item.IsRecentlyAdded);
            var row = Assert.IsType<ListViewItem>(list.ItemContainerGenerator.ContainerFromItem(latest));
            var position = row.TranslatePoint(new Point(), list);
            Assert.True(position.Y >= 0 && position.Y + row.ActualHeight <= list.ActualHeight + 1);
            Assert.Equal(products[^1].Id, latest.ProductId);
            Assert.Contains("상품 15", main.ActivityFeedbackText);
            CaptureQa(window, "main-latest-scan-900");
        }
        finally { window.Close(); }
    });

    private static void CaptureScaledRender(Window window, string name, double scale)
    {
        var output = Environment.GetEnvironmentVariable("SIMPLEPOS_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        // Render DPI coverage only; this does not change Windows display scaling or emulate physical input.
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale),
            (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(stream);
    }
}
