using InventoryManagement.Shared.DTOs;
using Microsoft.AspNetCore.Components.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;

namespace InventoryManagement.Client.Services;

public class AuthStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _http;
    private readonly ILocalStorageService _storage;
    private const string TokenKey = "auth_token";

    public AuthStateProvider(HttpClient http, ILocalStorageService storage)
    {
        _http = http;
        _storage = storage;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _storage.GetItemAsync(TokenKey);

        if (string.IsNullOrWhiteSpace(token))
            return Unauthenticated();

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
            return Unauthenticated();

        JwtSecurityToken jwt;
        try { jwt = handler.ReadJwtToken(token); }
        catch { return Unauthenticated(); }

        if (jwt.ValidTo < DateTime.UtcNow)
        {
            await _storage.RemoveItemAsync(TokenKey);
            return Unauthenticated();
        }

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var identity = new ClaimsIdentity(jwt.Claims, "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public async Task LoginAsync(AuthResponse response)
    {
        await _storage.SetItemAsync(TokenKey, response.Token);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", response.Token);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task LogoutAsync()
    {
        await _storage.RemoveItemAsync(TokenKey);
        _http.DefaultRequestHeaders.Authorization = null;
        NotifyAuthenticationStateChanged(Task.FromResult(Unauthenticated()));
    }

    private static AuthenticationState Unauthenticated() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));
}
