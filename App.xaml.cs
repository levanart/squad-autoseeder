using System.Windows;
using Autoseeder.Client.Services;

namespace Autoseeder.Client;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;
    private AppLog? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new SingleInstanceService("5thMR.Autoseeder.Client");
        if (!_singleInstance.IsPrimary)
        {
            if (e.Args.FirstOrDefault() is { } forwardedArgument)
                _singleInstance.Forward(forwardedArgument);
            Shutdown();
            return;
        }

        _log = new AppLog();
        DispatcherUnhandledException += (_, args) =>
        {
            _log.Write(args.Exception, "Необработанная ошибка интерфейса");
        };
        ProtocolRegistration.Register();
        var scheduledStart = e.Args.Any(argument => string.Equals(argument, "--scheduled-start", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(_log, scheduledStart);
        _singleInstance.MessageReceived += window.HandleActivation;
        window.Show();

        if (!scheduledStart && e.Args.FirstOrDefault() is { } argument)
            window.HandleActivation(argument);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _log?.Dispose();
        base.OnExit(e);
    }
}
