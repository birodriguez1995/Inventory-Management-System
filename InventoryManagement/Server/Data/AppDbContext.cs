using InventoryManagement.Server.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Server.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor httpContextAccessor)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Product>(e =>
        {
            e.HasIndex(p => p.SKU).IsUnique();
            e.Property(p => p.UnitPrice).HasColumnType("decimal(18,2)");
        });

        builder.Entity<StockMovement>(e =>
        {
            e.HasOne(sm => sm.Product)
             .WithMany(p => p.Movements)
             .HasForeignKey(sm => sm.ProductId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Property(sm => sm.Type).HasConversion<string>();
        });
    }

    public override int SaveChanges()
    {
        ApplyAuditTrail();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditTrail();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditTrail()
    {
        var currentUser = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "system";

        foreach (var entry in ChangeTracker.Entries<Product>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedBy = currentUser;
                entry.Entity.UpdatedBy = currentUser;
                entry.Entity.CreatedAt = DateTime.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedBy = currentUser;
            }
        }
    }
}
