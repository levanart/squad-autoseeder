using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Autoseeder.Client.Models;

namespace Autoseeder.Client.Services;

internal sealed class AuthService
{
    public const string BaseUrl = "https://5thmr.ru";
    private readonly TokenStore _store = new();
    public AuthState? Current { get; private set; }

    /// <summary>Сессия отозвана сервером — требуется повторный вход. Вызывается из фонового потока.</summary>
    public event Action? SessionExpired;

    public AuthService() => Current = _store.Load();

    public void StartLogin() => Process.Start(new ProcessStartInfo
    {
        FileName = $"{BaseUrl}/api/auth/steam/login?redirect={Uri.EscapeDataString("autoseeder://auth")}",
        UseShellExecute = true
    });

    public bool CompleteLogin(string callback)
    {
        if (!Uri.TryCreate(callback, UriKind.Absolute, out var uri) || uri.Scheme != "autoseeder") return false;
        var values = uri.Fragment.TrimStart('#').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2))
            .Where(x => x.Length == 2)
            .ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]), StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("token", out var access) || !values.TryGetValue("refresh", out var refresh)) return false;

        using var claims = ReadClaims(access);
        var root = claims.RootElement;
        var steamId = root.TryGetProperty("steam_id", out var steamIdValue) ? steamIdValue.GetString() ?? string.Empty : string.Empty;
        var username = root.TryGetProperty("unique_name", out var usernameValue) ? usernameValue.GetString() ?? steamId : steamId;
        Current = new AuthState(access, refresh, steamId, username);
        _store.Save(Current);
        return true;
    }

    public async Task<string> GetValidAccessToken(CancellationToken cancellationToken = default)
    {
        var state = Current ?? throw new SessionExpiredException("Требуется вход через Steam.");
        // Нечитаемый access-токен не считаем фатальным: пробуем обновиться по refresh-токену.
        if (TryGetExpiry(state.AccessToken) is { } expiresAt && expiresAt > DateTime.UtcNow.AddMinutes(2))
            return state.AccessToken;

        var cookies = new CookieContainer();
        var baseUri = new Uri(BaseUrl);
        cookies.Add(baseUri, new Cookie("discord5thmr.refresh", state.RefreshToken, "/", baseUri.Host));
        using var handler = new HttpClientHandler { CookieContainer = cookies };
        using var client = new HttpClient(handler);
        using var response = await client.PostAsync($"{BaseUrl}/api/auth/refresh", null, cancellationToken);

        // 401/403 — refresh-токен отозван или просрочен: повторять бессмысленно, сессию надо сбросить.
        // Прочие коды (5xx, 429) считаем временными и сессию сохраняем, чтобы пережить перезапуск сервера.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            InvalidateSession();
            throw new SessionExpiredException("Сессия истекла — войдите через Steam заново.");
        }

        response.EnsureSuccessStatusCode();
        var access = cookies.GetCookies(baseUri)["discord5thmr.auth"]?.Value;
        var refresh = cookies.GetCookies(baseUri)["discord5thmr.refresh"]?.Value;
        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh))
        {
            InvalidateSession();
            throw new SessionExpiredException("Сервер не вернул обновлённую сессию.");
        }
        Current = state with { AccessToken = access, RefreshToken = refresh };
        _store.Save(Current);
        return access;
    }

    public void Logout() { Current = null; _store.Clear(); }

    private void InvalidateSession()
    {
        if (Current is null) return;
        Current = null;
        _store.Clear();
        SessionExpired?.Invoke();
    }

    private static DateTime? TryGetExpiry(string token)
    {
        try
        {
            using var claims = ReadClaims(token);
            return claims.RootElement.TryGetProperty("exp", out var exp)
                ? DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()).UtcDateTime
                : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            return null;
        }
    }

    private static JsonDocument ReadClaims(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) throw new InvalidOperationException("Сервер вернул некорректный токен.");
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
    }
}

/// <summary>Сессия недействительна и не восстанавливается автоматически — нужен повторный вход через Steam.</summary>
internal sealed class SessionExpiredException(string message) : Exception(message);
