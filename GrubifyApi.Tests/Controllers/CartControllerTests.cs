using GrubifyApi.Controllers;
using GrubifyApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrubifyApi.Tests.Controllers;

public class CartControllerTests
{
    [Fact]
    public void AddItemToCart_RepeatedCalls_DoNotRetainLargePerRequestAllocations()
    {
        var controller = new CartController();
        var userId = Guid.NewGuid().ToString();
        var request = new AddCartItemRequest
        {
            FoodItemId = 1,
            Quantity = 1,
            SpecialInstructions = "Extra basil"
        };

        var baselineMemory = GetManagedMemory();

        for (var i = 0; i < 25; i++)
        {
            var result = controller.AddItemToCart(userId, request);
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var cart = Assert.IsType<Cart>(okResult.Value);

            Assert.Single(cart.Items);
            Assert.Equal(i + 1, cart.Items[0].Quantity);
        }

        var retainedMemory = GetManagedMemory() - baselineMemory;

        Assert.True(
            retainedMemory < 50 * 1024 * 1024,
            $"Expected repeated add-to-cart calls to avoid retaining large per-request buffers, but retained {retainedMemory / (1024 * 1024)} MB.");
    }

    private static long GetManagedMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
