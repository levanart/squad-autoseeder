using System.Globalization;

namespace Autoseeder.Client.Services;

internal sealed class ScheduledStartService
{
    private const string TaskName = "5thMR Autoseeder Scheduled Start";

    public void Schedule(DateTime localStart, bool wakeToRun)
    {
        if (localStart <= DateTime.Now.AddMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(localStart), "Время запуска должно быть минимум на минуту позже текущего.");

        dynamic service = CreateService();
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "Запускает 5thMR Autoseeder в режиме автосидинга.";
        definition.Settings.Enabled = true;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.WakeToRun = wakeToRun;
        definition.Settings.ExecutionTimeLimit = "PT0S";
        definition.Settings.MultipleInstances = 2;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Principal.LogonType = 3;
        definition.Principal.RunLevel = 0;

        dynamic trigger = definition.Triggers.Create(1);
        trigger.StartBoundary = localStart.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        trigger.Enabled = true;

        dynamic action = definition.Actions.Create(0);
        action.Path = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь приложения.");
        action.Arguments = "--scheduled-start";
        action.WorkingDirectory = AppContext.BaseDirectory;

        folder.RegisterTaskDefinition(TaskName, definition, 6, null, null, 3, null);
    }

    public void Remove()
    {
        dynamic service = CreateService();
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        try { folder.DeleteTask(TaskName, 0); }
        catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x80070002) { }
    }

    private static dynamic CreateService()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new PlatformNotSupportedException("Планировщик заданий Windows недоступен.");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Не удалось подключиться к планировщику заданий Windows.");
    }
}
