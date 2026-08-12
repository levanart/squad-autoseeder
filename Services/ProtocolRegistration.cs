using Microsoft.Win32;

namespace Autoseeder.Client.Services;

internal static class ProtocolRegistration
{
    public static void Register()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь приложения.");
        using var protocol = Registry.CurrentUser.CreateSubKey(@"Software\Classes\autoseeder");
        protocol.SetValue(string.Empty, "URL:5thMR Autoseeder Protocol");
        protocol.SetValue("URL Protocol", string.Empty);
        using var command = protocol.CreateSubKey(@"shell\open\command");
        command.SetValue(string.Empty, $"\"{executable}\" \"%1\"");
    }
}
