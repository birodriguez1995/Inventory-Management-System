using InventoryManagement.Server.Controllers;
using InventoryManagement.Server.Data;
using InventoryManagement.Server.Models;
using InventoryManagement.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace InventoryManagement.Tests;

/// <summary>
/// Unit tests for stock movement validation in <see cref="ProductsController"/>.
/// Focus: the invariant that an Outbound movement must not bring stock below 0.
/// </summary>
public class StockMovementValidationTests
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        return new AppDbContext(options, httpContextAccessor.Object);
    }

    private static async Task<(ProductsController Controller, AppDbContext Db)>
        BuildAsync(string dbName, int initialStock = 20)
    {
        var db = CreateDb(dbName);
        var product = new Product
        {
            Name = "Test Widget",
            SKU = "SKU-001",
            Category = "Test",
            QuantityInStock = initialStock,
            UnitPrice = 9.99m
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var controller = new ProductsController(db, NullLogger<ProductsController>.Instance);
        return (controller, db);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Inbound movements
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inbound_Movement_IncreasesStock()
    {
        var (controller, db) = await BuildAsync(nameof(Inbound_Movement_IncreasesStock), initialStock: 5);
        var productId = (await db.Products.FirstAsync()).Id;

        var request = new CreateStockMovementRequest { Type = "Inbound", Quantity = 10 };
        var result = await controller.RegisterMovement(productId, request);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<StockMovementDto>(ok.Value);
        Assert.Equal(10, dto.Quantity);
        Assert.Equal("Inbound", dto.Type);

        var updated = await db.Products.FindAsync(productId);
        Assert.Equal(15, updated!.QuantityInStock);
    }

    [Fact]
    public async Task Inbound_Movement_ZeroStock_StillSucceeds()
    {
        var (controller, db) = await BuildAsync(nameof(Inbound_Movement_ZeroStock_StillSucceeds), initialStock: 0);
        var productId = (await db.Products.FirstAsync()).Id;

        var request = new CreateStockMovementRequest { Type = "Inbound", Quantity = 50 };
        var result = await controller.RegisterMovement(productId, request);

        Assert.IsType<OkObjectResult>(result.Result);
        var updated = await db.Products.FindAsync(productId);
        Assert.Equal(50, updated!.QuantityInStock);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Outbound movements — happy path
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Outbound_Movement_DecreasesStock()
    {
        var (controller, db) = await BuildAsync(nameof(Outbound_Movement_DecreasesStock), initialStock: 30);
        var productId = (await db.Products.FirstAsync()).Id;

        var request = new CreateStockMovementRequest { Type = "Outbound", Quantity = 10 };
        var result = await controller.RegisterMovement(productId, request);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<StockMovementDto>(ok.Value);
        Assert.Equal(10, dto.Quantity);
        Assert.Equal("Outbound", dto.Type);

        var updated = await db.Products.FindAsync(productId);
        Assert.Equal(20, updated!.QuantityInStock);
    }

    [Fact]
    public async Task Outbound_ExactStock_Succeeds_And_ReachesZero()
    {
        var (controller, db) = await BuildAsync(nameof(Outbound_ExactStock_Succeeds_And_ReachesZero), initialStock: 10);
        var productId = (await db.Products.FirstAsync()).Id;

        var request = new CreateStockMovementRequest { Type = "Outbound", Quantity = 10 };
        var result = await controller.RegisterMovement(productId, request);

        Assert.IsType<OkObjectResult>(result.Result);
        var updated = await db.Products.FindAsync(productId);
        Assert.Equal(0, updated!.QuantityInStock);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Outbound movements — prevention of negative stock (business invariant)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Outbound_More_Than_Available_Returns_UnprocessableEntity()
    {
        var (controller, db) = await BuildAsync(
            nameof(Outbound_More_Than_Available_Returns_UnprocessableEntity), initialStock: 5);
        var productId = (await db.Products.FirstAsync()).Id;

        var request = new CreateStockMovementRequest { Type = "Outbound", Quantity = 10 };
        var result = await controller.RegisterMovement(productId, request);

        Assert.IsType<UnprocessableEntityObjectResult>(result.Result);

        // Stock must remain unchanged
        var updated = await db.Products.FindAsync(productId);
        Assert.Equal(5, updated!.QuantityInStock);
    }

    [Fact]
    public async Task Outbound_From_Zero_Stock_Returns_UnprocessableEntity()
    {
        var (controller, db) = await BuildAsync(
            nameof(Outbound_From_Zero_Stock_Returns_UnprocessableEntity), initialStock: 0);
        var productId = (await db.Products.FirstAsync()).Id;

        var request = new CreateStockMovementRequest { Type = "Outbound", Quantity = 1 };
        var result = await controller.RegisterMovement(productId, request);

        Assert.IsType<UnprocessableEntityObjectResult>(result.Result);

        var updated = await db.Products.FindAsync(productId);
        Assert.Equal(0, updated!.QuantityInStock);
    }

    [Theory]
    [InlineData(1, 2)]  // 1 in stock, trying to take 2
    [InlineData(9, 10)] // 9 in stock, trying to take 10
    [InlineData(0, 1)]  // 0 in stock, trying to take 1
    public async Task Outbound_NegativeStock_Always_Rejected(int stock, int requested)
    {
        var dbName = $"{nameof(Outbound_NegativeStock_Always_Rejected)}_{stock}_{requested}";
        var (controller, db) = await BuildAsync(dbName, initialStock: stock);
        var productId = (await db.Products.FirstAsync()).Id;

        var request = new CreateStockMovementRequest { Type = "Outbound", Quantity = requested };
        var result = await controller.RegisterMovement(productId, request);

        Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        var updated = await db.Products.FindAsync(productId);
        Assert.Equal(stock, updated!.QuantityInStock); // unchanged
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Invalid movement type
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invalid_MovementType_Returns_BadRequest()
    {
        var (controller, db) = await BuildAsync(nameof(Invalid_MovementType_Returns_BadRequest));
        var productId = (await db.Products.FirstAsync()).Id;

        var request = new CreateStockMovementRequest { Type = "Transfer", Quantity = 5 };
        var result = await controller.RegisterMovement(productId, request);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Non-existent product
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Movement_On_NonExistent_Product_Returns_NotFound()
    {
        var (controller, _) = await BuildAsync(
            nameof(Movement_On_NonExistent_Product_Returns_NotFound));

        var request = new CreateStockMovementRequest { Type = "Inbound", Quantity = 1 };
        var result = await controller.RegisterMovement(9999, request);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Case-insensitive movement type parsing
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("inbound")]
    [InlineData("INBOUND")]
    [InlineData("Inbound")]
    public async Task MovementType_Is_CaseInsensitive(string type)
    {
        var dbName = $"{nameof(MovementType_Is_CaseInsensitive)}_{type}";
        var (controller, db) = await BuildAsync(dbName);
        var productId = (await db.Products.FirstAsync()).Id;

        var request = new CreateStockMovementRequest { Type = type, Quantity = 1 };
        var result = await controller.RegisterMovement(productId, request);

        Assert.IsType<OkObjectResult>(result.Result);
    }
}
