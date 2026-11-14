using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Pos.App.ViewModels;
using Pos.App.Views;
using Pos.Infrastructure;

namespace Pos.App.Tests;

public sealed partial class PosWorkflowTests
{
    [Fact]
    public Task SharedEditorControlsKeepNativeAutomationAndSaveEditedValues() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var repository = new ExcelPosRepository(fixture.Paths.DefaultDatabasePath);
        await repository.SaveProductAsync(fixture.Product with { Category = "유제품" });
        await ((AsyncRelayCommand)fixture.Shell.NavigateProductEditorCommand).ExecuteAsync();
        var editor = Assert.IsType<ProductEditorViewModel>(fixture.Shell.CurrentPage);
        editor.ProductName = "직접 편집한 상품";
        editor.PriceText = "2000";
        var page = new ProductEditorPage { DataContext = editor };
        var window = TestWindow(page, fixture.Shell);
        window.Width = 1280;
        window.Height = 800;
        Popup? popup = null;
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var category = Descendants<ComboBox>(page).Single(combo => combo.Tag as string == "Category");
            var peer = Assert.IsType<ComboBoxAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(category));
            var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
            Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);

            // Exercise the native automation pattern: a styled arrow alone is insufficient.
            expand.Expand();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            popup = Assert.IsType<Popup>(category.Template.FindName("PART_Popup", category));
            Assert.True(popup.IsOpen);
            var popupContent = Assert.IsAssignableFrom<FrameworkElement>(popup.Child);
            Assert.True(popupContent.IsVisible);
            Assert.True(popupContent.ActualHeight > 0);
            Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
            var item = Assert.IsType<ComboBoxItem>(category.ItemContainerGenerator.ContainerFromItem("유제품"));
            Assert.True(item.IsVisible);
            // WPF exposes SelectionItem on the data-item peer owned by the selector,
            // not on the visual ComboBoxItem wrapper's element peer.
            var itemPeer = Assert.Single(peer.GetChildren(), child => child.GetName() == "유제품");
            Assert.IsAssignableFrom<ISelectionItemProvider>(itemPeer.GetPattern(PatternInterface.SelectionItem)).Select();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("유제품", editor.CategoryText);

            expand.Collapse();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(popup.IsOpen);
            Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);

            // Writing through the editable template part exercises ComboBox's Text plumbing.
            var categoryInput = Assert.IsType<TextBox>(category.Template.FindName("PART_EditableTextBox", category));
            Assert.True(categoryInput.IsVisible);
            categoryInput.Focus();
            categoryInput.Text = "매장 한정";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("매장 한정", category.Text);
            Assert.Equal("매장 한정", editor.CategoryText);

            var quickAdd = Descendants<CheckBox>(page).Single(box => box.Content as string == "빠른 추가에 표시");
            var togglePeer = Assert.IsType<CheckBoxAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(quickAdd));
            var toggle = Assert.IsAssignableFrom<IToggleProvider>(togglePeer.GetPattern(PatternInterface.Toggle));
            Assert.Equal(ToggleState.Off, toggle.ToggleState);
            toggle.Toggle();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(editor.IsQuickAdd);
            Assert.Equal(ToggleState.On, toggle.ToggleState);
            CaptureQa(window, "shared-editor-controls-1280");

            await ((AsyncRelayCommand)editor.SaveProductCommand).ExecuteAsync();
            var saved = (await repository.GetProductsAsync()).Single(product => product.Name == "직접 편집한 상품");
            Assert.Equal("매장 한정", saved.Category);
            Assert.True(saved.QuickAdd);
        }
        finally
        {
            if (popup is not null) popup.IsOpen = false;
            window.Close();
        }
    });

    [Fact]
    public Task SharedDataGridKeepsSelectionBindingAndFocusableCells() => Run(async () =>
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        await ((AsyncRelayCommand)fixture.Shell.NavigateProductListCommand).ExecuteAsync();
        var list = Assert.IsType<ProductListViewModel>(fixture.Shell.CurrentPage);
        var page = new ProductListPage { DataContext = list };
        var window = TestWindow(page, fixture.Shell);
        window.Width = 1280;
        window.Height = 800;
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = Descendants<DataGrid>(page).Single();
            var product = Assert.Single(list.Products);
            grid.SelectedItem = product;
            var priceColumn = grid.Columns.Single(column => column.Header as string == "가격");
            grid.CurrentCell = new DataGridCellInfo(product, priceColumn);
            grid.ScrollIntoView(product, priceColumn);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(product));
            Assert.True(row.IsSelected);
            Assert.Same(product, list.SelectedProduct);
            var cell = Descendants<DataGridCell>(row).Single(candidate => candidate.Column == priceColumn);
            cell.Focus();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(cell, FocusManager.GetFocusedElement(window));
            Assert.Equal(priceColumn, grid.CurrentCell.Column);
            var gridPeer = Assert.IsType<DataGridAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(grid));
            var selection = Assert.IsAssignableFrom<ISelectionProvider>(gridPeer.GetPattern(PatternInterface.Selection));
            Assert.Single(selection.GetSelection());
            CaptureQa(window, "shared-grid-focus-1280");

            // Moving focus to an action must preserve the selected product for checkout.
            var add = Descendants<Button>(page).Single(button => button.Content as string == "장바구니 담기");
            add.Focus();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(add, FocusManager.GetFocusedElement(window));
            Assert.True(row.IsSelected);
            Assert.Same(product, list.SelectedProduct);
            Assert.True(list.AddToCartCommand.CanExecute(null));
        }
        finally { window.Close(); }
    });
}
