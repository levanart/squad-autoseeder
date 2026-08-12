using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Autoseeder.Client.Services;

internal sealed class ClientRuntimeService : IDisposable
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "5thMR", "Autoseeder", "client-settings.json");
    private readonly object _sync = new();
    private Timer? _shutdownTimer;

    public string DeviceId { get; private set; } = Guid.NewGuid().ToString("N");
    public DateTime? ShutdownAtUtc { get; private set; }
    public string ScheduledAction { get; private set; } = "shutdown_pc";
    public event Action<DateTime?>? ShutdownScheduleChanged;

    public ClientRuntimeService()
    {
        Load();
        ArmTimer();
    }

    public bool IsSquadRunning() => GetSquadProcesses().Count > 0;

    public async Task CloseSquadAsync()
    {
        var processes = GetSquadProcesses();
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (process.CloseMainWindow() && await WaitForExitAsync(process, TimeSpan.FromSeconds(5)))
                        continue;
                    process.Kill(entireProcessTree: true);
                    await WaitForExitAsync(process, TimeSpan.FromSeconds(5));
                }
                catch (InvalidOperationException) { }
            }
        }
    }

    public void SetShutdown(DateTime shutdownAtUtc, string scheduledAction = "shutdown_pc")
    {
        if (shutdownAtUtc <= DateTime.UtcNow.AddSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(shutdownAtUtc), "Время выключения должно быть в будущем.");
        lock (_sync)
        {
            ShutdownAtUtc = shutdownAtUtc;
            ScheduledAction = scheduledAction == "close_squad" ? "close_squad" : "shutdown_pc";
            Save();
            ArmTimer();
        }
        ShutdownScheduleChanged?.Invoke(ShutdownAtUtc);
    }

    public void CancelShutdown()
    {
        lock (_sync)
        {
            ShutdownAtUtc = null;
            _shutdownTimer?.Dispose();
            _shutdownTimer = null;
            Save();
        }
        ShutdownScheduleChanged?.Invoke(null);
    }

    public static void ShutdownComputer()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = "/s /t 0",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private void ArmTimer()
    {
        _shutdownTimer?.Dispose();
        _shutdownTimer = null;
        if (ShutdownAtUtc is not { } target) return;
        var due = target - DateTime.UtcNow;
        if (due <= TimeSpan.Zero)
        {
            ShutdownAtUtc = null;
            Save();
            return;
        }
        _shutdownTimer = new Timer(async _ =>
        {
            if (ScheduledAction == "close_squad")
                await CloseSquadAsync();
            else
                ShutdownComputer();
        }, null, due, Timeout.InfiniteTimeSpan);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) { Save(); return; }
            var settings = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(_settingsPath));
            if (settings is null) return;
            DeviceId = string.IsNullOrWhiteSpace(settings.DeviceId) ? DeviceId : settings.DeviceId;
            ShutdownAtUtc = settings.ShutdownAtUtc > DateTime.UtcNow ? settings.ShutdownAtUtc : null;
            ScheduledAction = settings.ScheduledAction == "close_squad" ? "close_squad" : "shutdown_pc";
        }
        catch (JsonException) { }
        catch (IOException) { }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new ClientSettings(DeviceId, ShutdownAtUtc, ScheduledAction)));
    }

    private static List<Process> GetSquadProcesses()
    {
        var names = new[] { "SquadGame", "SquadGame_BE", "Squad", "SquadGame-Win64-Shipping" };
        return names.SelectMany(Process.GetProcessesByName)
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var stop = new CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(stop.Token); return true; }
        catch (OperationCanceledException) { return false; }
    }

    public void Dispose() => _shutdownTimer?.Dispose();

    private sealed record ClientSettings(string DeviceId, DateTime? ShutdownAtUtc, string? ScheduledAction);
}
