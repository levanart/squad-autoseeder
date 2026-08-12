using System.Windows;
using Autoseeder.Client.Services;

namespace Autoseeder.Client;

public partial class App : Application
{
    private SingleInstanceService? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new SingleInstanceService("5thMR.Autoseeder.Client");
        if (!_singleInstance.IsPrimary)
        {
            if (e.Args.FirstOrDefault() is { } argument)
                _singleInstance.Forward(argument);
            Shutdown();
            return;
        }

        ProtocolRegistration.Register();
        var window = new MainWindow();
        _singleInstance.MessageReceived += window.HandleProtocolUri;
        window.Show();

        if (e.Args.FirstOrDefault() is { } uri)
            window.HandleProtocolUri(uri);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
