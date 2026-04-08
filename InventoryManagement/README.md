# Inventory Management System

A full-stack inventory management application built with ASP.NET Core 8 + Blazor WebAssembly.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| API | ASP.NET Core 8 Web API |
| UI | Blazor WebAssembly (hosted) |
| Database | SQLite via EF Core 8 |
| Auth | ASP.NET Core Identity + JWT Bearer |
| Logging | Serilog (console + rolling file, machine name + thread enrichers) |
| Tests | xUnit + Moq |

## Projects

```
InventoryManagement/
├── Server/     — ASP.NET Core host: REST API + serves Blazor WASM
├── Client/     — Blazor WebAssembly SPA
├── Shared/     — DTOs shared between Client and Server
└── Tests/      — xUnit unit tests
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- _(Optional)_ Docker + Docker Compose

---

## Run Locally (single command)

```bash
dotnet run --project InventoryManagement/Server/InventoryManagement.Server.csproj
```

Then open **http://localhost:5000** in a browser.

The database (`inventory.db`) is created automatically on first run via EF Core migrations.  
A default admin user is seeded on startup:

| Field | Value |
|-------|-------|
| Email | `admin@inventory.local` |
| Password | `Admin1234!` |

---

## Run with Docker

```bash
cd InventoryManagement
docker-compose up --build
```

The app will be available at **http://localhost:8080**.  
The SQLite database is persisted in a named Docker volume (`db_data`).

---

## Run Tests

```bash
dotnet test InventoryManagement/Tests/InventoryManagement.Tests.csproj
```

14 unit tests covering:
- Inbound / outbound stock movements
- Negative stock prevention
- Exact-zero stock boundary
- Invalid movement type → `400 Bad Request`
- Non-existent product → `404 Not Found`
- Case-insensitive movement type parsing

---

## API Endpoints

All endpoints (except auth) require `Authorization: Bearer <token>`.

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/auth/register` | Register a new user |
| `POST` | `/api/auth/login` | Obtain a JWT token |
| `GET` | `/api/products` | List products (optional: `?category=X&lowStockThreshold=N`) |
| `GET` | `/api/products/{id}` | Get a single product |
| `POST` | `/api/products` | Create a product |
| `PUT` | `/api/products/{id}` | Update a product |
| `DELETE` | `/api/products/{id}` | Delete a product |
| `GET` | `/api/products/{id}/movements` | List stock movements for a product |
| `POST` | `/api/products/{id}/movements` | Register a stock movement |

Swagger UI is available at **http://localhost:5000/swagger** (Development only).

---

## Configuration

`appsettings.json` / environment variables:

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:DefaultConnection` | `Data Source=inventory.db` | SQLite path |
| `JwtSettings:Secret` | _(see appsettings.json)_ | **Change in production** |
| `JwtSettings:ExpiryHours` | `8` | Token lifetime |
| `LowStockAlert:Threshold` | `10` | Units below which a warning is logged |
| `LowStockAlert:IntervalMinutes` | `5` | How often the background alert service polls |

> **Security**: Replace `JwtSettings:Secret` with a strong random value before deploying to production.

---

## Features

- **Product CRUD**: name, SKU (unique), category, quantity, unit price
- **Stock movements**: Inbound / Outbound with reason notes; prevents stock from going negative
- **Audit trail**: `CreatedBy` / `UpdatedBy` fields auto-populated from the authenticated user
- **Low-stock alerts**: Background service logs a warning for every product below threshold
- **Filter UI**: products list filterable by category and low-stock threshold
- **Authentication**: JWT with `admin@inventory.local` / `Admin1234!` seeded on first run
- **Logging**: Serilog writes structured logs to console and `logs/inventory-.txt` (rolling daily)
