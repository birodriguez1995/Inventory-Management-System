using Microsoft.AspNetCore.Identity;

namespace InventoryManagement.Server.Models;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
}
