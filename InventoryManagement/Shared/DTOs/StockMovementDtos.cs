using System.ComponentModel.DataAnnotations;

namespace InventoryManagement.Shared.DTOs;

public record StockMovementDto(
    int Id,
    int ProductId,
    string ProductName,
    string Type,
    int Quantity,
    string? Reason,
    DateTime Timestamp
);

public class CreateStockMovementRequest
{
    [Required]
    public string Type { get; set; } = string.Empty; // "Inbound" or "Outbound"

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
    public int Quantity { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}
