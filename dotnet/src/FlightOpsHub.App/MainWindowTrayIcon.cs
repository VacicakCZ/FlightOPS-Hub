using System.Windows.Forms;
using Icon = System.Drawing.Icon;

namespace FlightOpsHub.App;

/// <summary>
/// General "minimize to tray" for the main window: minimizing hides the
/// window from the taskbar and shows this icon with Show/Exit instead.
/// Re-adds the "minimize to tray" half of the feature that shipped once
/// after v2.2.0 and was fully reverted after a reproducible silent crash
/// on the second minimize/restore cycle (see [[flightops-tray-feature-reverted]]
/// memory) - that was pystray, a third-party library running its own win32
/// message loop. This uses System.Windows.Forms.NotifyIcon instead (same
/// fix already proven for the unrelated launch/watch tray in TrayIcon.cs):
/// one persistent icon for the app's whole lifetime, created once here and
/// only ever toggled visible/hidden, so there is no repeated native
/// create/destroy cycle to race.
///
/// Also carries the tray quick-launch menu (Launch + flight/exe profile
/// submenus, see TrayProfileLauncher.cs) - built fresh every time the icon
/// is shown, so a profile added/renamed/deleted while the window was open
/// is reflected the next time it's minimized, without needing its own
/// change-tracking.
/// </summary>
public class MainWindowTrayIcon : IDisposable
{
    private const int MaxTextLength = 63; // NotifyIcon.Text's hard limit

    private readonly NotifyIcon _icon;
    private readonly Func<string, string> _tr;
    private readonly TrayProfileLauncher _launcher;
    private readonly Action _onShow;
    private readonly Action _onExit;

    public MainWindowTrayIcon(string iconPath, Func<string, string> translate, TrayProfileLauncher launcher, Action onShow, Action onExit)
    {
        _tr = translate;
        _launcher = launcher;
        _onShow = onShow;
        _onExit = onExit;

        _icon = new NotifyIcon
        {
            Icon = new Icon(iconPath),
            Visible = false,
        };
        // Click (not DoubleClick) matches how most Windows tray icons
        // restore their app; right-click still opens ContextMenuStrip on
        // its own without going through this handler.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _onShow();
        };
        RefreshText();
    }

    /// <summary>Re-reads translated strings (the language can change while minimized) and shows the icon.</summary>
    public void Show()
    {
        RefreshText();
        _icon.Visible = true;
    }

    public void Hide() => _icon.Visible = false;

    private void RefreshText()
    {
        _icon.Text = Truncate(_tr("tray_minimized_tooltip"));
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(_tr("tray_launch_label"), null, (_, _) => _launcher.Launch()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(BuildProfileSubmenu(_tr("tray_flight_profile_menu"), _launcher.FlightProfileNames(), _launcher.SelectedFlightProfile(), _launcher.SelectFlightProfile));
        menu.Items.Add(BuildProfileSubmenu(_tr("tray_exe_profile_menu"), _launcher.ExeProfileNames(), _launcher.SelectedExeProfile(), _launcher.SelectExeProfile));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(_tr("tray_minimized_show"), null, (_, _) => _onShow()));
        menu.Items.Add(new ToolStripMenuItem(_tr("tray_minimized_exit"), null, (_, _) => _onExit()));
        _icon.ContextMenuStrip = menu;
    }

    /// <summary>Picking an item only remembers the choice (see TrayProfileLauncher's own doc comment) - it takes effect the next time Launch is clicked, never immediately.</summary>
    private ToolStripMenuItem BuildProfileSubmenu(string label, IReadOnlyList<string> names, string? selected, Action<string> onSelect)
    {
        var submenu = new ToolStripMenuItem(label);
        if (names.Count == 0)
        {
            submenu.DropDownItems.Add(new ToolStripMenuItem(_tr("tray_no_profiles")) { Enabled = false });
            return submenu;
        }
        foreach (var name in names)
        {
            var item = new ToolStripMenuItem(name) { Checked = name == selected };
            item.Click += (_, _) => onSelect(name);
            submenu.DropDownItems.Add(item);
        }
        return submenu;
    }

    private static string Truncate(string text) => text.Length <= MaxTextLength ? text : text[..MaxTextLength];

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
