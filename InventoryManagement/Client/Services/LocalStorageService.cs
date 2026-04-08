using Microsoft.JSInterop;

namespace InventoryManagement.Client.Services;

public interface ILocalStorageService
{
    ValueTask<string?> GetItemAsync(string key);
    ValueTask SetItemAsync(string key, string value);
    ValueTask RemoveItemAsync(string key);
}

public class LocalStorageService : ILocalStorageService
{
    private readonly IJSRuntime _js;

    public LocalStorageService(IJSRuntime js) => _js = js;

    public ValueTask<string?> GetItemAsync(string key) =>
        _js.InvokeAsync<string?>("localStorage.getItem", key);

    public ValueTask SetItemAsync(string key, string value) =>
        _js.InvokeVoidAsync("localStorage.setItem", key, value);

    public ValueTask RemoveItemAsync(string key) =>
        _js.InvokeVoidAsync("localStorage.removeItem", key);
}
