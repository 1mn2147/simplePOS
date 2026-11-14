using Pos.Core;

namespace Pos.Tests;

public sealed class CartUndoTests
{
    [Fact]
    public void ClearCanRestoreProductsQuantitiesAndOrder()
    {
        var first = new Product { Name = "우유", Price = 1500 };
        var second = new Product { Name = "생수", Price = 500 };
        var cart = new CartService();
        cart.Add(first, 3);
        cart.Add(second, 2);
        cart.Clear();

        Assert.True(cart.IsEmpty);
        Assert.True(cart.UndoRemoval());
        Assert.Equal(new[] { first.Id, second.Id }, cart.Lines.Select(line => line.ProductId));
        Assert.Equal(5, cart.ItemCount);
        Assert.Equal(5500, cart.Total);
        Assert.False(cart.UndoRemoval());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RemoveAndDecrementToZeroCanBeUndone(bool decrement)
    {
        var product = new Product { Name = "상품", Price = 900, Stock = 1 };
        var cart = new CartService();
        cart.Add(product);
        if (decrement) cart.Decrement(product.Id);
        else cart.Remove(product.Id);

        Assert.True(cart.UndoRemoval());
        Assert.Equal(1, Assert.Single(cart.Lines).Quantity);
    }

    [Fact]
    public void NewCartMutationInvalidatesOldUndoButBlockedAdditionDoesNot()
    {
        var product = new Product { Name = "상품", Price = 900 };
        var cart = new CartService();
        cart.Add(product);
        cart.Clear();
        cart.Add(product with { Stock = 0 });
        Assert.True(cart.CanUndoRemoval);
        cart.Add(product);
        Assert.False(cart.CanUndoRemoval);
    }

    [Fact]
    public void CompletedSaleCannotBeRestoredAsAnUnpaidCart()
    {
        var cart = new CartService();
        cart.Add(new Product { Name = "상품", Price = 900 });
        cart.Clear();
        cart.UndoRemoval();
        cart.Clear(allowUndo: false);

        Assert.False(cart.UndoRemoval());
        Assert.True(cart.IsEmpty);
    }
}
