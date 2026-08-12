using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Reflection;
using Autoseeder.Client.Models;
using Autoseeder.Client.Services;

namespace Autoseeder.Client;

public partial class MainWindow : Window
{
    private readonly AuthService _auth = new();
    private readonly AutoseederHubClient _hub;
    private readonly ClientRuntimeService _runtime = new();
    private readonly ObservableCollection<AutoseederServer> _servers = [];
    private readonly ObservableCollection<string> _logs = [];
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _memoryHistory = new();
    private readonly Dictionary<string, Queue<double>> _gpuHistories = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<MetricDisplay> _systemMetrics = [];
    private readonly CancellationTokenSource _hardwareStop = new();
    private HardwareMonitorService? _hardwareMonitor;
    private bool _enabled;
    private string? _lastOpenedServer;

    public MainWindow()
    {
        InitializeComponent();
        ServersList.ItemsSource = _servers;
        LogsList.ItemsSource = _logs;
        SystemMetricsList.ItemsSource = _systemMetrics;
        _hub = new AutoseederHubClient(_auth);
        _hub.StatusChanged += status => Dispatcher.Invoke(() => ApplyStatus(status));
        _hub.Log += message => Dispatcher.Invoke(() => AddLog(message));
        _hub.CommandReceived += HandleRemoteCommandAsync;
        _runtime.ShutdownScheduleChanged += value => Dispatcher.Invoke(() => UpdateShutdownSchedule(value));
        UpdateAccount();
        UpdateShutdownSchedule(_runtime.ShutdownAtUtc);
        Closing += async (_, _) =>
        {
            _hardwareStop.Cancel();
            _hardwareMonitor?.Dispose();
            _runtime.Dispose();
            await _hub.DisposeAsync();
        };
        StateChanged += (_, _) => ApplyMaximizedBounds();
        _ = MonitorHardwareAsync(_hardwareStop.Token);
    }

    public void HandleProtocolUri(string value) => Dispatcher.Invoke(async () =>
    {
        Activate();
        WindowState = WindowState.Normal;
        if (_auth.CompleteLogin(value))
        {
            AddLog("Вход через Steam выполнен");
            UpdateAccount();
        }
        else AddLog("Не удалось завершить вход через Steam");
        await Task.CompletedTask;
    });

    private void LoginClick(object sender, RoutedEventArgs e) { AddLog("Открыта страница входа Steam"); _auth.StartLogin(); }
    private async void LogoutClick(object sender, RoutedEventArgs e) { await StopAsync(); _auth.Logout(); _servers.Clear(); UpdateAccount(); AddLog("Сессия завершена"); }
    private async void ToggleClick(object sender, RoutedEventArgs e) { if (_enabled) await StopAsync(); else await StartAsync(); }
    private async void RefreshClick(object sender, RoutedEventArgs e) => await RunBusy(async () => await _hub.RefreshAsync());
    private void TitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private async Task StartAsync()
    {
        await RunBusy(async () =>
        {
            AddLog("Запуск автосидера…");
            _enabled = true;
            ToggleButton.Content = "Остановить";
            StateText.Text = "Автосидер работает";
            ShowActiveBadge();
            await _hub.StartAsync();
        }, resetOnError: true);
    }

    private async Task StopAsync()
    {
        _enabled = false;
        _lastOpenedServer = null;
        ToggleButton.Content = "Запустить";
        StateText.Text = "Автосидер выключен";
        ActiveBadge.Visibility = Visibility.Collapsed;
        TargetText.Text = "Ожидание запуска";
        await RunBusy(async () => await _hub.StopAsync());
        AddLog("Автосидер остановлен");
    }

    private void ApplyStatus(AutoseederStatus status)
    {
        AddLog($"Получено обновление данных: серверов {status.Servers.Count}, порог {status.SeedThreshold}");
        _servers.Clear();
        foreach (var server in status.Servers.OrderBy(x => x.SeedOrder)) _servers.Add(server);
        var target = status.Servers.FirstOrDefault(x => x.IsSelected);
        TargetText.Text = target is null ? "Сид сейчас не требуется" : $"Цель: {target.Name}";
        if (!_enabled || target?.JoinUrl is not { Length: > 0 } joinUrl) return;
        if (string.Equals(status.UserServerKey, target.Key, StringComparison.OrdinalIgnoreCase)) { _lastOpenedServer = null; return; }
        if (string.Equals(_lastOpenedServer, target.Key, StringComparison.OrdinalIgnoreCase)) return;
        _lastOpenedServer = target.Key;
        AddLog($"Подключение к {target.Name}");
        Process.Start(new ProcessStartInfo { FileName = joinUrl, UseShellExecute = true });
    }

    private async Task RunBusy(Func<Task> action, bool resetOnError = false)
    {
        ToggleButton.IsEnabled = false; RefreshButton.IsEnabled = false;
        try { await action(); }
        catch (Exception ex)
        {
            AddLog($"Ошибка: {ex.Message}");
            if (resetOnError) { _enabled = false; ToggleButton.Content = "Запустить"; StateText.Text = "Ошибка запуска"; ActiveBadge.Visibility = Visibility.Collapsed; }
        }
        finally { ToggleButton.IsEnabled = _auth.Current is not null; RefreshButton.IsEnabled = _auth.Current is not null; }
    }

    private void UpdateAccount()
    {
        var account = _auth.Current;
        var loggedIn = account is not null;
        AccountText.Text = account?.Username ?? "Steam не подключён";
        AccountSubtext.Text = account?.SteamId ?? "Авторизуйтесь для запуска";
        AccountDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(loggedIn ? "#FF29D67C" : "#FF676B74"));
        LoginButton.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        LogoutButton.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        ToggleButton.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        ToggleButton.IsEnabled = loggedIn;
        RefreshButton.IsEnabled = loggedIn;
        if (!_enabled)
            TargetText.Text = loggedIn ? "Готов к запуску" : "Войдите через Steam, чтобы начать";
    }

    private void AddLog(string message)
    {
        _logs.Add($"[{DateTime.Now:HH:mm:ss}]  {message}");
        while (_logs.Count > 200) _logs.RemoveAt(0);
        LogsList.ScrollIntoView(_logs.LastOrDefault());
    }

    private void ShowActiveBadge()
    {
        ActiveBadge.Opacity = 0;
        ActiveBadge.Visibility = Visibility.Visible;
        ActiveBadge.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Восстановить" : "Развернуть";
    }

    private void ApplyMaximizedBounds()
    {
        if (WindowState != WindowState.Maximized) return;
        var area = SystemParameters.WorkArea;
        MaxWidth = area.Width;
        MaxHeight = area.Height;
    }

    private async Task MonitorHardwareAsync(CancellationToken cancellationToken)
    {
        try
        {
            _hardwareMonitor = new HardwareMonitorService();
            while (!cancellationToken.IsCancellationRequested)
            {
                var snapshot = await Task.Run(_hardwareMonitor.Read, cancellationToken);
                ApplyHardwareSnapshot(snapshot);
                if (_auth.Current is not null)
                {
                    try
                    {
                        await _hub.ReportTelemetryAsync(new ClientTelemetryReport(
                            _runtime.DeviceId,
                            Environment.MachineName,
                            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
                            _enabled,
                            _runtime.IsSquadRunning(),
                            _runtime.ShutdownAtUtc,
                            _runtime.ScheduledAction,
                            new ClientTelemetryPoint(
                                DateTime.UtcNow,
                                snapshot.CpuLoad,
                                snapshot.CpuTemperature,
                                snapshot.MemoryLoad,
                                snapshot.MemoryUsedGigabytes,
                                snapshot.MemoryTotalGigabytes,
                                snapshot.Gpus.Select(gpu => new ClientGpuTelemetry(
                                    gpu.Name, gpu.Load, gpu.Temperature)).ToArray())));
                    }
                    catch (Exception ex)
                    {
                        AddLog($"Telemetry не отправлена: {ex.Message}");
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AddLog($"Датчики системы недоступны: {ex.Message}");
        }
    }

    private void ApplyHardwareSnapshot(HardwareSnapshot snapshot)
    {
        AppendHistory(_cpuHistory, snapshot.CpuLoad);
        AppendHistory(_memoryHistory, snapshot.MemoryLoad);

        _systemMetrics.Clear();
        _systemMetrics.Add(new MetricDisplay(
            "CPU",
            FormatPercent(snapshot.CpuLoad),
            FormatTemperature(snapshot.CpuTemperature),
            _cpuHistory.ToArray(),
            Brush("#FFFF7A1A"),
            Brush("#FFFFB15C")));
        _systemMetrics.Add(new MetricDisplay(
            "ОПЕРАТИВНАЯ ПАМЯТЬ",
            FormatPercent(snapshot.MemoryLoad),
            FormatMemory(snapshot.MemoryUsedGigabytes, snapshot.MemoryTotalGigabytes),
            _memoryHistory.ToArray(),
            Brush("#FF29D67C"),
            Brush("#FF4AE697")));
        for (var index = 0; index < snapshot.Gpus.Count; index++)
        {
            var gpu = snapshot.Gpus.ElementAt(index);
            if (!_gpuHistories.TryGetValue(gpu.Name, out var history))
            {
                history = new Queue<double>();
                _gpuHistories[gpu.Name] = history;
            }
            AppendHistory(history, gpu.Load);
            var stroke = index % 2 == 0 ? "#FFFBBF24" : "#FF7DD3FC";
            _systemMetrics.Add(new MetricDisplay(
                gpu.Name.ToUpperInvariant(),
                FormatPercent(gpu.Load),
                FormatTemperature(gpu.Temperature),
                history.ToArray(),
                Brush(stroke),
                Brush(stroke)));
        }
    }

    private static void AppendHistory(Queue<double> history, double? value)
    {
        history.Enqueue(Math.Clamp(value ?? 0, 0, 100));
        while (history.Count > 60) history.Dequeue();
    }

    private static string FormatPercent(double? value) => value.HasValue ? $"{value.Value:0.0} %" : "— %";
    private static string FormatTemperature(double? value) => value.HasValue ? $"{value.Value:0.0} °C" : "— °C";
    private static string FormatMemory(double? used, double? total) =>
        used.HasValue && total.HasValue ? $"{used.Value:0.0} / {total.Value:0.0} ГБ" : "— / — ГБ";

    private async Task HandleRemoteCommandAsync(ClientCommandMessage command)
    {
        var succeeded = false;
        string? error = null;
        try
        {
            switch (command.Command)
            {
                case "start_seeding":
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        if (!_enabled)
                            await StartAsync();
                    }).Task.Unwrap();
                    break;
                case "stop_seeding":
                    await Dispatcher.InvokeAsync(async () => await StopAsync()).Task.Unwrap();
                    break;
                case "close_squad":
                    await _runtime.CloseSquadAsync();
                    break;
                case "shutdown_pc":
                    await _hub.AcknowledgeCommandAsync(new ClientCommandAcknowledgement(
                        command.RequestId, _runtime.DeviceId, command.Command, true, null));
                    ClientRuntimeService.ShutdownComputer();
                    return;
                case "set_shutdown" when command.ShutdownAtUtc.HasValue:
                    _runtime.SetShutdown(command.ShutdownAtUtc.Value, command.ScheduledAction ?? "shutdown_pc");
                    break;
                case "cancel_shutdown":
                    _runtime.CancelShutdown();
                    break;
                default:
                    throw new InvalidOperationException("Неизвестная команда");
            }
            succeeded = true;
            AddLog($"Выполнена удалённая команда: {command.Command}");
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AddLog($"Ошибка удалённой команды: {ex.Message}");
        }

        await _hub.AcknowledgeCommandAsync(new ClientCommandAcknowledgement(
            command.RequestId, _runtime.DeviceId, command.Command, succeeded, error));
    }

    private void ScheduleShutdownClick(object sender, RoutedEventArgs e)
    {
        if (ShutdownDatePicker.SelectedDate is not { } date ||
            !TimeSpan.TryParse(ShutdownTimeText.Text, out var time))
        {
            AddLog("Укажите дату и время выключения");
            return;
        }

        var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Local);
        try { _runtime.SetShutdown(local.ToUniversalTime(), GetScheduledAction()); }
        catch (ArgumentOutOfRangeException ex) { AddLog(ex.Message); }
    }

    private void ScheduleShutdownDelayClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ShutdownDelayMinutesText.Text, out var minutes) || minutes <= 0)
        {
            AddLog("Укажите положительное количество минут");
            return;
        }

        try { _runtime.SetShutdown(DateTime.UtcNow.AddMinutes(minutes), GetScheduledAction()); }
        catch (ArgumentOutOfRangeException ex) { AddLog(ex.Message); }
    }

    private void CancelShutdownClick(object sender, RoutedEventArgs e) => _runtime.CancelShutdown();

    private void UpdateShutdownSchedule(DateTime? shutdownAtUtc)
    {
        if (shutdownAtUtc is { } utc)
        {
            var local = utc.ToLocalTime();
            ShutdownDatePicker.SelectedDate = local.Date;
            ShutdownTimeText.Text = local.ToString("HH:mm");
            ShutdownStatusText.Text = _runtime.ScheduledAction == "close_squad"
                ? $"Squad закроется {local:dd.MM.yyyy в HH:mm}"
                : $"ПК выключится {local:dd.MM.yyyy в HH:mm}";
            CloseSquadTimerRadio.IsChecked = _runtime.ScheduledAction == "close_squad";
            ShutdownPcTimerRadio.IsChecked = _runtime.ScheduledAction != "close_squad";
            CancelShutdownButton.IsEnabled = true;
        }
        else
        {
            ShutdownDatePicker.SelectedDate = DateTime.Today;
            ShutdownTimeText.Text = DateTime.Now.AddHours(1).ToString("HH:mm");
            ShutdownStatusText.Text = "Таймер не запланирован";
            CancelShutdownButton.IsEnabled = false;
        }
    }

    private string GetScheduledAction() =>
        CloseSquadTimerRadio.IsChecked == true ? "close_squad" : "shutdown_pc";

    private static SolidColorBrush Brush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));

    private sealed record MetricDisplay(
        string Title,
        string Value,
        string Secondary,
        double[] Values,
        SolidColorBrush Stroke,
        SolidColorBrush SecondaryBrush);
}
