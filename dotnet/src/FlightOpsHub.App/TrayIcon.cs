using System.Windows.Forms;
using Icon = System.Drawing.Icon;

namespace FlightOpsHub.App;

/// <summary>
/// Port of backend/tray.py - system tray icon shown while a launch's Smart
/// Launch wait or "stay and watch" loop is running. Has zero relationship
/// to the main WPF window.
///
/// Backed by System.Windows.Forms.NotifyIcon (first-party, WinForms-interop,
/// UseWindowsForms enabled in the csproj) rather than a third-party library -
/// this is the actual fix for the crash that started this whole rewrite
/// (see [[flightops-tray-feature-reverted]] memory): that was pystray, a
/// third-party library with its own win32 message-loop implementation,
/// dying silently on a second minimize/restore cycle. NotifyIcon is
/// Microsoft's own, and every mutation here happens on the WPF UI thread's
/// message loop (via Dispatcher, from LaunchSession), never a second
/// competing one.
/// </summary>
public class TrayIcon : IDisposable
{
    private const int MaxTextLength = 63; // NotifyIcon.Text's hard limit

    private readonly string _iconPath;
    private NotifyIcon? _icon;
    private Icon? _iconImage;
    private ContextMenuStrip? _menu;

    public TrayIcon(string iconPath)
    {
        _iconPath = iconPath;
    }

    /// <summary>Must be called on the WPF UI thread.</summary>
    public void Ensure(string tooltip)
    {
        if (_icon != null) return;
        try
        {
            _iconImage = new Icon(_iconPath);
            _menu = BuildMenu(tooltip, null, null);
            _icon = new NotifyIcon
            {
                Icon = _iconImage,
                Text = Truncate(tooltip),
                Visible = true,
                ContextMenuStrip = _menu,
            };
        }
        catch
        {
            _icon = null;
            _menu?.Dispose();
            _menu = null;
            _iconImage?.Dispose();
            _iconImage = null;
        }
    }

    /// <summary>Must be called on the WPF UI thread.</summary>
    public void SetMenu(string statusText, string actionText, Action actionCallback)
    {
        if (_icon is null) return;
        try
        {
            var newMenu = BuildMenu(statusText, actionText, actionCallback);
            _icon.Text = Truncate(statusText);
            _icon.ContextMenuStrip = newMenu;
            _menu?.Dispose();
            _menu = newMenu;
        }
        catch
        {
            // Best-effort, matches Python's bare except here.
        }
    }

    /// <summary>Must be called on the WPF UI thread.</summary>
    public void Stop()
    {
        if (_icon is null) return;
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch
        {
            // Best-effort, matches Python's bare except here.
        }
        _icon = null;
        _menu?.Dispose();
        _menu = null;
        _iconImage?.Dispose();
        _iconImage = null;
    }

    public void Dispose() => Stop();

    private static ContextMenuStrip BuildMenu(string statusText, string? actionText, Action? actionCallback)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(statusText) { Enabled = false });
        if (actionText != null && actionCallback != null)
        {
            menu.Items.Add(new ToolStripMenuItem(actionText, null, (_, _) => actionCallback()));
        }
        return menu;
    }

    private static string Truncate(string text) => text.Length <= MaxTextLength ? text : text[..MaxTextLength];
}
