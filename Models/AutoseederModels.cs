namespace Autoseeder.Client.Models;

public sealed record AutoseederStatus(
    int SeedThreshold,
    IReadOnlyCollection<AutoseederServer> Servers);

public sealed record AutoseederServer(
    string Key,
    string Name,
    int SeedOrder,
    int? Online,
    int? Queue,
    int SeedThreshold,
    bool ShouldSeed,
    bool IsSelected,
    bool IsCurrentUserPresent,
    string? JoinUrl,
    string Status)
{
    public string PlayerLabel => Online is null ? "—" : $"{Online} игроков";
    public string QueueLabel => Queue is > 0 ? $"Очередь: {Queue}" : string.Empty;
    public string StatusLabel => Status switch
    {
        "unavailable" or "offline" => "Недоступен",
        "online_unknown" => "Обновление данных",
        "error" => "Ошибка получения статуса",
        _ when ShouldSeed => IsSelected ? "Целевой сервер" : "Нужен сид",
        _ => "Засижен"
    };
    public string StatusColor => Status switch
    {
        "unavailable" or "offline" or "error" => "#FFFF5C5C",
        "online_unknown" => "#FFFF9A4A",
        _ when ShouldSeed => "#FFFBBF24",
        _ => "#FF29D67C"
    };
    public string CardBorder => IsSelected ? "#70FBBF24" : ShouldSeed ? "#45FBBF24" : "#24FFFFFF";
    public string CardBackground => IsSelected ? "#18FBBF24" : ShouldSeed ? "#0EFBBF24" : "#0CFFFFFF";
}

public sealed record AuthState(string AccessToken, string RefreshToken, string SteamId, string Username);
