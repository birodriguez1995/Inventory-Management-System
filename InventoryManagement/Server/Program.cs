using InventoryManagement.Server.Data;
using InventoryManagement.Server.Models;
using InventoryManagement.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;

// ── Bootstrap Serilog early so startup errors are captured ──────────────────
Log.Logger = new LoggerConfiguration()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/inventory-.log", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/inventory-.log", rollingInterval: RollingInterval.Day));

    // ── EF Core / SQLite ─────────────────────────────────────────────────────
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite(
            builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=inventory.db"));

    builder.Services.AddHttpContextAccessor();

    // ── ASP.NET Core Identity ────────────────────────────────────────────────
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

    // ── JWT Authentication ───────────────────────────────────────────────────
    var jwtSection = builder.Configuration.GetSection("JwtSettings");
    var secret = jwtSection["Secret"]
        ?? throw new InvalidOperationException("JwtSettings:Secret is required.");

    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSection["Issuer"],
                ValidAudience = jwtSection["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
            };
        });

    builder.Services.AddAuthorization();

    // ── MVC / API ────────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddRazorPages();

    // ── Swagger ──────────────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "Inventory API", Version = "v1" });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Bearer — paste your token here",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                        { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });
    });

    // ── Background services ──────────────────────────────────────────────────
    builder.Services.AddHostedService<LowStockAlertService>();

    var app = builder.Build();

    // ── Auto-migrate & seed ──────────────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();

        await SeedAsync(scope.ServiceProvider);
    }

    // ── Middleware pipeline ──────────────────────────────────────────────────
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Inventory API v1"));
        app.UseWebAssemblyDebugging();
    }

    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();
    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapRazorPages();
    app.MapFallbackToFile("index.html");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application start-up failed.");
}
finally
{
    Log.CloseAndFlush();
}

// ── Seed a default admin user + demo products ────────────────────────────
static async Task SeedAsync(IServiceProvider services)
{
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var db = services.GetRequiredService<AppDbContext>();

    // ── Admin user ───────────────────────────────────────────────────────────
    const string email = "admin@inventory.local";
    const string password = "Admin1234!";

    if (await userManager.FindByEmailAsync(email) is null)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Admin"
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            Log.Warning("Seed user creation failed: {Errors}", errors);
        }
        else
        {
            Log.Information("Seeded default admin user: {Email}", email);
        }
    }

    // ── Demo products (only if table is empty) ───────────────────────────────
    if (db.Products.Any()) return;

    var now = DateTime.UtcNow;

    var products = new List<InventoryManagement.Server.Models.Product>
    {
        new() { Name = "Laptop Dell XPS 15",         SKU = "LAPTOP-DXPS15",  Category = "Electronics",    QuantityInStock = 12,  UnitPrice = 1_499.99m, CreatedAt = now.AddDays(-30), CreatedBy = email },
        new() { Name = "Monitor LG 27\" 4K",          SKU = "MON-LG27-4K",   Category = "Electronics",    QuantityInStock = 8,   UnitPrice = 499.99m,   CreatedAt = now.AddDays(-28), CreatedBy = email },
        new() { Name = "Teclado Mecánico Logitech",   SKU = "KBD-LOGI-MX",   Category = "Peripherals",    QuantityInStock = 35,  UnitPrice = 129.99m,   CreatedAt = now.AddDays(-25), CreatedBy = email },
        new() { Name = "Mouse Inalámbrico MX Master", SKU = "MSE-LOGI-MXM3", Category = "Peripherals",    QuantityInStock = 3,   UnitPrice = 99.99m,    CreatedAt = now.AddDays(-25), CreatedBy = email },
        new() { Name = "Silla Ergonómica Herman Miller",SKU = "CHR-HM-AERON", Category = "Furniture",      QuantityInStock = 6,   UnitPrice = 1_299.00m, CreatedAt = now.AddDays(-20), CreatedBy = email },
        new() { Name = "Escritorio Standing Flexispot",SKU = "DSK-FLEX-E7",  Category = "Furniture",      QuantityInStock = 4,   UnitPrice = 589.00m,   CreatedAt = now.AddDays(-18), CreatedBy = email },
        new() { Name = "Auriculares Sony WH-1000XM5", SKU = "AUD-SNY-XM5",   Category = "Electronics",    QuantityInStock = 0,   UnitPrice = 349.99m,   CreatedAt = now.AddDays(-15), CreatedBy = email },
        new() { Name = "Webcam Logitech C920",         SKU = "CAM-LOGI-C920", Category = "Peripherals",    QuantityInStock = 9,   UnitPrice = 79.99m,    CreatedAt = now.AddDays(-12), CreatedBy = email },
        new() { Name = "Hub USB-C Anker 10-en-1",      SKU = "HUB-ANK-10C1", Category = "Accessories",    QuantityInStock = 22,  UnitPrice = 49.99m,   CreatedAt = now.AddDays(-10), CreatedBy = email },
        new() { Name = "Disco SSD Samsung 1TB",        SKU = "SSD-SAM-1TB",   Category = "Storage",        QuantityInStock = 17,  UnitPrice = 89.99m,    CreatedAt = now.AddDays(-8),  CreatedBy = email },
        new() { Name = "Memoria RAM Kingston 32GB",    SKU = "RAM-KNG-32DDR5",Category = "Components",     QuantityInStock = 7,   UnitPrice = 119.99m,   CreatedAt = now.AddDays(-7),  CreatedBy = email },
        new() { Name = "Cable HDMI 2.1 2m",            SKU = "CBL-HDMI21-2M", Category = "Accessories",    QuantityInStock = 50,  UnitPrice = 19.99m,    CreatedAt = now.AddDays(-5),  CreatedBy = email },
    };

    db.Products.AddRange(products);
    await db.SaveChangesAsync();

    // ── Demo stock movements ─────────────────────────────────────────────────
    var laptop  = products[0];
    var monitor = products[1];
    var mouse   = products[3];
    var headset = products[6];
    var webcam  = products[7];
    var ram     = products[10];

    var movements = new List<InventoryManagement.Server.Models.StockMovement>
    {
        // Laptop — varios ingresos y salidas
        new() { ProductId = laptop.Id,  Type = InventoryManagement.Server.Models.MovementType.Inbound,  Quantity = 20, Reason = "Compra inicial proveedor Dell",    Timestamp = now.AddDays(-30) },
        new() { ProductId = laptop.Id,  Type = InventoryManagement.Server.Models.MovementType.Outbound, Quantity = 5,  Reason = "Entrega departamento Marketing",   Timestamp = now.AddDays(-20) },
        new() { ProductId = laptop.Id,  Type = InventoryManagement.Server.Models.MovementType.Outbound, Quantity = 3,  Reason = "Entrega departamento Ventas",      Timestamp = now.AddDays(-10) },

        // Monitor — algo de rotación
        new() { ProductId = monitor.Id, Type = InventoryManagement.Server.Models.MovementType.Inbound,  Quantity = 15, Reason = "Reposición trimestral",            Timestamp = now.AddDays(-28) },
        new() { ProductId = monitor.Id, Type = InventoryManagement.Server.Models.MovementType.Outbound, Quantity = 7,  Reason = "Equipamiento oficina nueva",       Timestamp = now.AddDays(-14) },

        // Mouse — casi sin stock
        new() { ProductId = mouse.Id,   Type = InventoryManagement.Server.Models.MovementType.Inbound,  Quantity = 10, Reason = "Pedido inicial",                   Timestamp = now.AddDays(-25) },
        new() { ProductId = mouse.Id,   Type = InventoryManagement.Server.Models.MovementType.Outbound, Quantity = 7,  Reason = "Distribución a empleados remotos", Timestamp = now.AddDays(-5)  },

        // Auriculares — agotado, último movimiento fue salida
        new() { ProductId = headset.Id, Type = InventoryManagement.Server.Models.MovementType.Inbound,  Quantity = 5,  Reason = "Pedido especial",                  Timestamp = now.AddDays(-15) },
        new() { ProductId = headset.Id, Type = InventoryManagement.Server.Models.MovementType.Outbound, Quantity = 5,  Reason = "Entrega sala de conferencias",     Timestamp = now.AddDays(-3)  },

        // Webcam — stock bajo
        new() { ProductId = webcam.Id,  Type = InventoryManagement.Server.Models.MovementType.Inbound,  Quantity = 20, Reason = "Compra masiva pandemia",           Timestamp = now.AddDays(-12) },
        new() { ProductId = webcam.Id,  Type = InventoryManagement.Server.Models.MovementType.Outbound, Quantity = 11, Reason = "Demanda trabajo remoto",           Timestamp = now.AddDays(-6)  },

        // RAM — reposición reciente
        new() { ProductId = ram.Id,     Type = InventoryManagement.Server.Models.MovementType.Inbound,  Quantity = 10, Reason = "Actualización equipos desarrollo",  Timestamp = now.AddDays(-7)  },
        new() { ProductId = ram.Id,     Type = InventoryManagement.Server.Models.MovementType.Outbound, Quantity = 3,  Reason = "Upgrade PCs equipo DevOps",        Timestamp = now.AddDays(-2)  },
    };

    db.StockMovements.AddRange(movements);
    await db.SaveChangesAsync();

    Log.Information("Seeded {ProductCount} demo products and {MovementCount} stock movements.",
        products.Count, movements.Count);
}
