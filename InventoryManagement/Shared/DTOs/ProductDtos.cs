using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Shared.DTOs;

public record ProductDto(
    int Id,
    string Name,
    string SKU,
    string Category,
    int QuantityInStock,
    decimal UnitPrice,
    DateTime CreatedAt,
    string? CreatedBy,
    string? UpdatedBy
);

public class CreateProductRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string SKU { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    [Range(0, int.MaxValue, ErrorMessage = "Quantity must be zero or greater.")]
    public int QuantityInStock { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Unit price must be greater than zero.")]
    public decimal UnitPrice { get; set; }
}

public class UpdateProductRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    [Range(0, int.MaxValue, ErrorMessage = "Quantity must be zero or greater.")]
    public int QuantityInStock { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Unit price must be greater than zero.")]
    public decimal UnitPrice { get; set; }
}
