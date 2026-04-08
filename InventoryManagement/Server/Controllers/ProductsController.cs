using InventoryManagement.Server.Data;
using InventoryManagement.Server.Models;
using InventoryManagement.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Server.Controllers;

[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(AppDbContext db, ILogger<ProductsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // GET /api/products?category=Electronics&lowStockThreshold=10
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetAll(
        [FromQuery] string? category,
        [FromQuery] int? lowStockThreshold)
    {
        var query = _db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => EF.Functions.Like(p.Category, $"%{category}%"));

        if (lowStockThreshold.HasValue)
            query = query.Where(p => p.QuantityInStock <= lowStockThreshold.Value);

        var products = await query
            .OrderBy(p => p.Name)
            .Select(p => ToDto(p))
            .ToListAsync();

        return Ok(products);
    }

    // GET /api/products/{id}
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetById(int id)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);

        if (product is null)
            return NotFound(new { message = $"Product with id {id} was not found." });

        return Ok(ToDto(product));
    }

    // POST /api/products
    [HttpPost]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> Create([FromBody] CreateProductRequest request)
    {
        if (await _db.Products.AnyAsync(p => p.SKU == request.SKU))
            return Conflict(new { message = $"A product with SKU '{request.SKU}' already exists." });

        var product = new Product
        {
            Name = request.Name,
            SKU = request.SKU,
            Category = request.Category,
            QuantityInStock = request.QuantityInStock,
            UnitPrice = request.UnitPrice
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Product created: {SKU} ({Name})", product.SKU, product.Name);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ToDto(product));
    }

    // PUT /api/products/{id}
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> Update(int id, [FromBody] UpdateProductRequest request)
    {
        var product = await _db.Products.FindAsync(id);

        if (product is null)
            return NotFound(new { message = $"Product with id {id} was not found." });

        product.Name = request.Name;
        product.Category = request.Category;
        product.QuantityInStock = request.QuantityInStock;
        product.UnitPrice = request.UnitPrice;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Product updated: {Id} ({SKU})", product.Id, product.SKU);
        return Ok(ToDto(product));
    }

    // DELETE /api/products/{id}
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await _db.Products.FindAsync(id);

        if (product is null)
            return NotFound(new { message = $"Product with id {id} was not found." });

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Product deleted: {Id} ({SKU})", id, product.SKU);
        return NoContent();
    }

    // POST /api/products/{id}/movements
    [HttpPost("{id:int}/movements")]
    [ProducesResponseType(typeof(StockMovementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StockMovementDto>> RegisterMovement(
        int id,
        [FromBody] CreateStockMovementRequest request)
    {
        var product = await _db.Products.FindAsync(id);

        if (product is null)
            return NotFound(new { message = $"Product with id {id} was not found." });

        if (!Enum.TryParse<MovementType>(request.Type, ignoreCase: true, out var movementType))
            return BadRequest(new { message = "Movement type must be 'Inbound' or 'Outbound'." });

        if (movementType == MovementType.Outbound && product.QuantityInStock < request.Quantity)
        {
            return UnprocessableEntity(new
            {
                message = $"Insufficient stock. Available: {product.QuantityInStock}, requested: {request.Quantity}."
            });
        }

        var movement = new StockMovement
        {
            ProductId = id,
            Type = movementType,
            Quantity = request.Quantity,
            Reason = request.Reason,
            Timestamp = DateTime.UtcNow
        };

        product.QuantityInStock += movementType == MovementType.Inbound
            ? request.Quantity
            : -request.Quantity;

        _db.StockMovements.Add(movement);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Stock movement {Type} of {Qty} registered for product {Id}. New stock: {Stock}",
            movementType, request.Quantity, id, product.QuantityInStock);

        return Ok(ToMovementDto(movement, product.Name));
    }

    // GET /api/products/{id}/movements
    [HttpGet("{id:int}/movements")]
    [ProducesResponseType(typeof(IEnumerable<StockMovementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<StockMovementDto>>> GetMovements(int id)
    {
        if (!await _db.Products.AnyAsync(p => p.Id == id))
            return NotFound(new { message = $"Product with id {id} was not found." });

        var movements = await _db.StockMovements
            .AsNoTracking()
            .Where(sm => sm.ProductId == id)
            .Include(sm => sm.Product)
            .OrderByDescending(sm => sm.Timestamp)
            .Select(sm => ToMovementDto(sm, sm.Product.Name))
            .ToListAsync();

        return Ok(movements);
    }

    private static ProductDto ToDto(Product p) =>
        new(p.Id, p.Name, p.SKU, p.Category, p.QuantityInStock, p.UnitPrice,
            p.CreatedAt, p.CreatedBy, p.UpdatedBy);

    private static StockMovementDto ToMovementDto(StockMovement sm, string productName) =>
        new(sm.Id, sm.ProductId, productName, sm.Type.ToString(), sm.Quantity, sm.Reason, sm.Timestamp);
}
