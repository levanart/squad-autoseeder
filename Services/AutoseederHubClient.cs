using Autoseeder.Client.Models;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Autoseeder.Client.Services;

internal sealed class AutoseederHubClient : IAsyncDisposable
{
    private readonly AuthService _auth;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly CancellationTokenSource _disposeToken = new();
    private HubConnection? _connection;
    private bool _seedingRequested;
    private int _recoveryRunning;
    public event Action<AutoseederStatus>? StatusChanged;
    public event Action<string>? Log;
    public event Func<ClientCommandMessage, Task>? CommandReceived;

    public AutoseederHubClient(AuthService auth) => _auth = auth;

    public async Task ConnectAsync()
    {
        await _connectionGate.WaitAsync(_disposeToken.Token);
        try
        {
            if (_connection?.State is HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting)
                return;

            if (_connection is null)
                _connection = CreateConnection();

            Log?.Invoke("Hub: подключение к серверу");
            await _connection.StartAsync(_disposeToken.Token);
            Log?.Invoke("Hub: соединение установлено");
        }
        catch
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
            throw;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private HubConnection CreateConnection()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl($"{AuthService.BaseUrl}/hubs/autoseeder", options =>
            {
                options.AccessTokenProvider = async () => await _auth.GetValidAccessToken();
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15) })
            .Build();
        connection.On<AutoseederStatus>("StatusUpdate", status => StatusChanged?.Invoke(status));
        connection.On("CacheUpdate", async () => await RefreshAsync());
        connection.On<ClientCommandMessage>("ClientCommand", async command =>
        {
            if (CommandReceived is not null)
                await CommandReceived(command);
        });
        connection.Reconnecting += error =>
        {
            Log?.Invoke($"Hub: соединение потеряно; встроенное переподключение начато{FormatError(error)}");
            return Task.CompletedTask;
        };
        connection.Reconnected += async _ =>
        {
            Log?.Invoke("Hub: соединение восстановлено; состояние автосидинга синхронизируется");
            if (_seedingRequested)
                await InvokeStartSeedingAsync();
        };
        connection.Closed += error =>
        {
            Log?.Invoke($"Hub: соединение закрыто после попыток восстановления{FormatError(error)}");
            if (_seedingRequested && !_disposeToken.IsCancellationRequested)
                _ = RecoverClosedConnectionAsync();
            return Task.CompletedTask;
        };
        return connection;
    }

    public async Task<AutoseederStatus> StartAsync()
    {
        _seedingRequested = true;
        await ConnectAsync();
        return await InvokeStartSeedingAsync();
    }

    private async Task<AutoseederStatus> InvokeStartSeedingAsync()
    {
        if (_connection?.State != HubConnectionState.Connected)
            throw new InvalidOperationException("Hub не подключён.");
        var status = await _connection!.InvokeAsync<AutoseederStatus>("StartSeeding");
        StatusChanged?.Invoke(status);
        return status;
    }

    public async Task<AutoseederStatus> RefreshAsync()
    {
        await ConnectAsync();
        var status = await _connection!.InvokeAsync<AutoseederStatus>("GetStatus");
        StatusChanged?.Invoke(status);
        return status;
    }

    public async Task StopAsync()
    {
        _seedingRequested = false;
        if (_connection is null) return;
        if (_connection.State == HubConnectionState.Connected)
            await _connection.InvokeAsync<AutoseederStatus>("StopSeeding");
    }

    public async Task ReportTelemetryAsync(ClientTelemetryReport report)
    {
        await ConnectAsync();
        await _connection!.InvokeAsync("ReportClientTelemetry", report);
    }

    public async Task AcknowledgeCommandAsync(ClientCommandAcknowledgement acknowledgement)
    {
        if (_connection?.State == HubConnectionState.Connected)
            await _connection.InvokeAsync("AcknowledgeClientCommand", acknowledgement);
    }

    private async Task DisconnectAsync()
    {
        if (_connection is null) return;
        await _connection.StopAsync();
        await _connection.DisposeAsync();
        _connection = null;
    }

    private async Task RecoverClosedConnectionAsync()
    {
        if (Interlocked.Exchange(ref _recoveryRunning, 1) != 0)
            return;
        try
        {
            var delays = new[] { 5, 15, 30, 60 };
            var attempt = 0;
            while (_seedingRequested && !_disposeToken.IsCancellationRequested)
            {
                var delaySeconds = delays[Math.Min(attempt, delays.Length - 1)];
                attempt++;
                Log?.Invoke($"Hub: фоновая попытка восстановления {attempt} через {delaySeconds} сек.");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _disposeToken.Token);
                try
                {
                    await ConnectAsync();
                    await InvokeStartSeedingAsync();
                    Log?.Invoke("Hub: фоновое восстановление завершено");
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log?.Invoke($"Hub: попытка восстановления {attempt} не удалась: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Interlocked.Exchange(ref _recoveryRunning, 0);
        }
    }

    private static string FormatError(Exception? error) =>
        error is null ? string.Empty : $": {error.GetType().Name}: {error.Message}";

    public async ValueTask DisposeAsync()
    {
        _seedingRequested = false;
        _disposeToken.Cancel();
        await DisconnectAsync();
        _disposeToken.Dispose();
        _connectionGate.Dispose();
    }
}
