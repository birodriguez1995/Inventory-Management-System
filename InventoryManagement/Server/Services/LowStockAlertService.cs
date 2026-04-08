using InventoryManagement.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace InventoryManagement.Server.Services;

public class LowStockAlertService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LowStockAlertService> _logger;

    public LowStockAlertService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<LowStockAlertService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Low-stock alert service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckLowStockAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during low-stock check.");
            }

            var intervalMinutes = _configuration.GetValue<double>("LowStockAlert:IntervalMinutes", 5);
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task CheckLowStockAsync(CancellationToken ct)
    {
        var threshold = _configuration.GetValue<int>("LowStockAlert:Threshold", 10);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lowStock = await db.Products
            .AsNoTracking()
            .Where(p => p.QuantityInStock <= threshold)
            .OrderBy(p => p.QuantityInStock)
            .ToListAsync(ct);

        if (lowStock.Count == 0)
        {
            _logger.LogDebug("Low-stock check: no products at or below threshold {Threshold}.", threshold);
            return;
        }

        foreach (var product in lowStock)
        {
            _logger.LogWarning(
                "LOW STOCK ALERT: [{SKU}] {Name} — {Qty} unit(s) remaining (threshold: {Threshold}).",
                product.SKU, product.Name, product.QuantityInStock, threshold);
        }
    }
}
