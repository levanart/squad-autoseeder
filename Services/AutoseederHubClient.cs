using Autoseeder.Client.Models;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Autoseeder.Client.Services;

internal sealed class AutoseederHubClient : IAsyncDisposable
{
    private readonly AuthService _auth;
    private HubConnection? _connection;
    private bool _seedingRequested;
    public event Action<AutoseederStatus>? StatusChanged;
    public event Action<string>? Log;
    public event Func<ClientCommandMessage, Task>? CommandReceived;

    public AutoseederHubClient(AuthService auth) => _auth = auth;

    public async Task ConnectAsync()
    {
        if (_connection is not null) return;
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
        connection.Reconnecting += _ => { Log?.Invoke("Соединение потеряно, переподключение…"); return Task.CompletedTask; };
        connection.Reconnected += async _ =>
        {
            Log?.Invoke("Соединение восстановлено");
            if (_seedingRequested)
                await InvokeStartSeedingAsync();
        };
        connection.Closed += _ => { Log?.Invoke("Соединение закрыто"); return Task.CompletedTask; };
        await connection.StartAsync();
        _connection = connection;
        Log?.Invoke("Подключено к серверу");
    }

    public async Task<AutoseederStatus> StartAsync()
    {
        _seedingRequested = true;
        await ConnectAsync();
        return await InvokeStartSeedingAsync();
    }

    private async Task<AutoseederStatus> InvokeStartSeedingAsync()
    {
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

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
