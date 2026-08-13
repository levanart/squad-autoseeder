namespace Autoseeder.Client.Services;

internal sealed class ReconnectStateMachine
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(45),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(5),
    ];

    private string? _targetKey;
    private int _attempt;
    private DateTimeOffset _nextAttemptAt;

    public bool ShouldOpen(string targetKey, bool isCurrentUserPresent, DateTimeOffset now, Action<string> log)
    {
        if (isCurrentUserPresent)
        {
            if (_targetKey is not null)
                log($"Reconnect: пользователь подтверждён на целевом сервере {targetKey}; ожидание сброшено");
            Reset();
            return false;
        }

        if (!string.Equals(_targetKey, targetKey, StringComparison.OrdinalIgnoreCase))
        {
            _targetKey = targetKey;
            _attempt = 0;
            _nextAttemptAt = now;
            log($"Reconnect: выбрана новая цель {targetKey}; подключение разрешено немедленно");
        }

        if (now < _nextAttemptAt)
        {
            log($"Reconnect: цель {targetKey} ещё не подтверждена; следующая попытка после {_nextAttemptAt:HH:mm:ss}");
            return false;
        }

        return true;
    }

    public void MarkOpened(string targetKey, DateTimeOffset now, Action<string> log)
    {
        var delay = RetryDelays[Math.Min(_attempt, RetryDelays.Length - 1)];
        _attempt++;
        _nextAttemptAt = now + delay;
        log($"Reconnect: ссылка цели {targetKey} открыта, попытка {_attempt}; cooldown {delay.TotalSeconds:0} сек.");
    }

    public void MarkFailed(string targetKey, DateTimeOffset now, Action<string> log)
    {
        var delay = TimeSpan.FromSeconds(10);
        _nextAttemptAt = now + delay;
        log($"Reconnect: не удалось открыть ссылку цели {targetKey}; повтор не раньше чем через {delay.TotalSeconds:0} сек.");
    }

    public void Clear(Action<string>? log = null)
    {
        if (_targetKey is not null)
            log?.Invoke("Reconnect: цель отсутствует или автосидер остановлен; состояние сброшено");
        Reset();
    }

    private void Reset()
    {
        _targetKey = null;
        _attempt = 0;
        _nextAttemptAt = default;
    }
}
