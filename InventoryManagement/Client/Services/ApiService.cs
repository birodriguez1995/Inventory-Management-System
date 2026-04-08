using InventoryManagement.Shared.DTOs;
using System.Net.Http.Json;
using System.Net;

namespace InventoryManagement.Client.Services;

public interface IApiService
{
    Task<List<ProductDto>> GetProductsAsync(string? category = null, int? lowStockThreshold = null);
    Task<ProductDto?> GetProductAsync(int id);
    Task<(ProductDto? Product, string? Error)> CreateProductAsync(CreateProductRequest request);
    Task<(ProductDto? Product, string? Error)> UpdateProductAsync(int id, UpdateProductRequest request);
    Task<(bool Success, string? Error)> DeleteProductAsync(int id);
    Task<(StockMovementDto? Movement, string? Error)> RegisterMovementAsync(int productId, CreateStockMovementRequest request);
    Task<List<StockMovementDto>> GetMovementsAsync(int productId);
    Task<(AuthResponse? Response, string? Error)> LoginAsync(LoginRequest request);
    Task<(bool Success, string? Error)> RegisterAsync(RegisterRequest request);
}

public class ApiService : IApiService
{
    private readonly HttpClient _http;

    public ApiService(HttpClient http) => _http = http;

    public async Task<List<ProductDto>> GetProductsAsync(string? category = null, int? lowStockThreshold = null)
    {
        var url = "api/products";
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(category)) qs.Add($"category={Uri.EscapeDataString(category)}");
        if (lowStockThreshold.HasValue) qs.Add($"lowStockThreshold={lowStockThreshold}");
        if (qs.Count > 0) url += "?" + string.Join("&", qs);

        return await _http.GetFromJsonAsync<List<ProductDto>>(url) ?? [];
    }

    public Task<ProductDto?> GetProductAsync(int id) =>
        _http.GetFromJsonAsync<ProductDto>($"api/products/{id}");

    public async Task<(ProductDto? Product, string? Error)> CreateProductAsync(CreateProductRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/products", request);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<ProductDto>(), null);
        return (null, await ExtractErrorAsync(response));
    }

    public async Task<(ProductDto? Product, string? Error)> UpdateProductAsync(int id, UpdateProductRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/products/{id}", request);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<ProductDto>(), null);
        return (null, await ExtractErrorAsync(response));
    }

    public async Task<(bool Success, string? Error)> DeleteProductAsync(int id)
    {
        var response = await _http.DeleteAsync($"api/products/{id}");
        return response.IsSuccessStatusCode
            ? (true, null)
            : (false, await ExtractErrorAsync(response));
    }

    public async Task<(StockMovementDto? Movement, string? Error)> RegisterMovementAsync(
        int productId, CreateStockMovementRequest request)
    {
        var response = await _http.PostAsJsonAsync($"api/products/{productId}/movements", request);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<StockMovementDto>(), null);
        return (null, await ExtractErrorAsync(response));
    }

    public Task<List<StockMovementDto>> GetMovementsAsync(int productId) =>
        _http.GetFromJsonAsync<List<StockMovementDto>>($"api/products/{productId}/movements")
             .ContinueWith(t => t.Result ?? []);

    public async Task<(AuthResponse? Response, string? Error)> LoginAsync(LoginRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", request);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<AuthResponse>(), null);
        return (null, await ExtractErrorAsync(response));
    }

    public async Task<(bool Success, string? Error)> RegisterAsync(RegisterRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/register", request);
        return response.IsSuccessStatusCode
            ? (true, null)
            : (false, await ExtractErrorAsync(response));
    }

    private static async Task<string> ExtractErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
            if (!string.IsNullOrWhiteSpace(body?.Message)) return body.Message;
            if (body?.Errors?.Any() == true) return string.Join("; ", body.Errors);
        }
        catch { /* fall through */ }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "You are not authorised. Please log in.",
            HttpStatusCode.NotFound => "The requested resource was not found.",
            HttpStatusCode.Conflict => "A conflict occurred (e.g. duplicate SKU).",
            HttpStatusCode.UnprocessableEntity => "Insufficient stock for this movement.",
            _ => $"Request failed ({(int)response.StatusCode})."
        };
    }

    private sealed record ErrorBody(string? Message, string[]? Errors);
}
