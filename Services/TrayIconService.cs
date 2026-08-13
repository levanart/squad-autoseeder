using System.Drawing;
using System.Windows.Forms;

namespace Autoseeder.Client.Services;

internal sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;

    public event Action? ShowRequested;
    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add("Запустить автосидер", null, (_, _) => StartRequested?.Invoke());
        menu.Items.Add("Остановить автосидер", null, (_, _) => StopRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выйти", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = "5thMR Autoseeder",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public void ShowHiddenNotice()
    {
        _icon.ShowBalloonTip(
            2500,
            "5thMR Autoseeder",
            "Приложение продолжает работать в области уведомлений.",
            ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
