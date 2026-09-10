using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using FlightOpsHub.Core;
using Microsoft.Web.WebView2.Core;

namespace FlightOpsHub.App;

public partial class MainWindow : Window
{
    // Matches the virtual host used in index.html-relative asset references
    // (frontend/ has no build step, so plain relative paths already work
    // once this is mapped - no rewriting of the existing HTML/JS needed).
    private const string VirtualHostName = "appassets.local";

    private readonly ApiDispatcher _dispatcher = new();
    private readonly ConfigService _configService;
    private readonly AppsService _appsService;
    private readonly ProfilesService _profilesService;
    private readonly ExeXmlService _exeXmlService;
    private readonly SettingsService _settingsService;
    private readonly BackupService _backupService;
    private readonly SceneryService _sceneryService;
    private readonly LaunchService _launchService;
    private readonly UpdateService _updateService;
    private readonly EventBus _events = new();
    private readonly GsxFolderWatcher _gsxWatcher;
    private readonly I18n _i18n;
    private readonly MainWindowTrayIcon _windowTray;
    private readonly TrayProfileLauncher _trayProfileLauncher;
    private readonly string _logPath;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"FlightOps Hub {AppVersion.Current}";

        // %LocalAppData%\FlightOpsHub, not the install folder - an
        // installed build can't assume its own folder is writable, and
        // keeping user data out of the program folder keeps uninstall/
        // upgrade unambiguous about what to remove. See AppPaths.cs.
        var configPath = Path.Combine(AppPaths.UserDataDirectory, ConfigManager.ConfigFileName);
        var appsPath = Path.Combine(AppPaths.UserDataDirectory, AppsManager.AppsFileName);
        _logPath = Path.Combine(AppPaths.UserDataDirectory, AppLogging.LogFileName);

        _configService = new ConfigService(configPath);
        _appsService = new AppsService(appsPath);
        _profilesService = new ProfilesService(_configService);
        _exeXmlService = new ExeXmlService(_configService);
        _settingsService = new SettingsService(_configService);
        _backupService = new BackupService(_configService, _appsService, _logPath);
        var aircraftDevelopersPath = Path.Combine(AppContext.BaseDirectory, "data", "aircraft_developers.json");
        var airportsPath = Path.Combine(AppContext.BaseDirectory, "data", "airports.json");
        _sceneryService = new SceneryService(_configService, aircraftDevelopersPath, airportsPath, _events.Emit);

        // The two fields backend/api.py's _config_snapshot() computes fresh
        // on every call rather than ever persisting - see
        // ConfigService.RegisterEffectiveField's doc comment. Registered
        // here, right after the services that own them exist, so every
        // config_get/config_set/settings_*/exe_*/import_config response
        // carries them from this point on.
        _configService.RegisterEffectiveField("_gsx_profiles_path_effective", _sceneryService.GsxPath);
        _configService.RegisterEffectiveField("_exe_xml_path_effective", _exeXmlService.CurrentPath);
        _configService.RegisterEffectiveField("_launch_at_startup_effective", StartupShortcut.IsEnabled);

        var localesDir = Path.Combine(AppContext.BaseDirectory, "frontend", "locales");
        var trayIconPath = Path.Combine(AppContext.BaseDirectory, "OIG3.ico");
        _i18n = new I18n(localesDir);
        _launchService = new LaunchService(this, _configService, _appsService, _i18n, trayIconPath);
        _updateService = new UpdateService(_events, _logPath);

        _trayProfileLauncher = new TrayProfileLauncher(_configService, _appsService, _profilesService, _exeXmlService, _launchService);
        _windowTray = new MainWindowTrayIcon(
            trayIconPath,
            key => _i18n.Translate(GetConfigLanguage(), key),
            _trayProfileLauncher,
            onShow: RestoreFromTray,
            onExit: Close);
        StateChanged += MainWindow_StateChanged;
        Closed += (_, _) => _windowTray.Dispose();

        RestoreWindowGeometry();
        Closing += (_, _) => SaveWindowGeometry();

        // Set before this constructor returns (and therefore before the
        // App-framework's StartupUri machinery ever calls Show()) so the
        // window goes straight from non-existent to minimized without
        // ever flashing open - the same "--minimized, straight to tray"
        // contract flightops_hub.pyw's own startup honors, for the
        // Startup-folder shortcut StartupShortcut.cs creates.
        if (Environment.GetCommandLineArgs().Contains("--minimized"))
        {
            WindowState = WindowState.Minimized;
            Loaded += (_, _) =>
            {
                Hide();
                _windowTray.Show();
            };
        }

        // Started once at startup, same as flightops_hub.pyw's
        // api.start_gsx_watcher() call right after bus.bind(window) - a
        // GSX folder change re-triggers a full reload/re-check via
        // config_changed, the same event a Community-path change fires.
        _gsxWatcher = new GsxFolderWatcher(_sceneryService.GsxPath, () => _events.Emit("config_changed"));

        AppLogging.Info(_logPath, $"FlightOps Hub {AppVersion.Current} starting");

        ApiRegistration.RegisterAll(
            _dispatcher, _configService, _appsService, _profilesService, _exeXmlService,
            _settingsService, _backupService, _sceneryService, _launchService, _updateService);

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // WebView2's default user data folder sits next to the exe, which
        // isn't guaranteed writable for an installed build (an admin-mode
        // install puts {app} under Program Files) and left a stray
        // "FlightOpsHub.exe.WebView2" folder behind after uninstall in
        // testing. Point it at the same writable per-user folder as
        // config/apps/log instead.
        var webView2UserDataFolder = Path.Combine(AppPaths.UserDataDirectory, "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: webView2UserDataFolder);
        await Browser.EnsureCoreWebView2Async(environment);

        var frontendDir = Path.Combine(AppContext.BaseDirectory, "frontend");
        Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            frontendDir,
            CoreWebView2HostResourceAccessKind.Allow);

        Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        // Runs before ANY page script, including frontend/js/api.js's first
        // line - that's what lets api.js tell this build apart from the
        // Python/pywebview one synchronously, with no timing race (see the
        // long comment at the top of api.js for why that race mattered).
        await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            "window.__flightOpsHubDotNetBridge = true;");

        Browser.Source = new Uri($"https://{VirtualHostName}/index.html");

        _events.Bind(Browser);
        _gsxWatcher.Start();
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var requestJson = e.TryGetWebMessageAsString();
        if (requestJson is null) return;

        // Must await here, not block on the sync Dispatch wrapper - this
        // runs on the WPF UI thread, and blocking on a Task whose
        // continuation (e.g. after an HttpClient call in SimbriefCheck)
        // needs that same thread would deadlock.
        var responseJson = await _dispatcher.DispatchAsync(requestJson);
        Browser.CoreWebView2.PostWebMessageAsJson(responseJson);
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _windowTray.Show();
        }
    }

    // Runs on the UI thread already - NotifyIcon's callbacks are pumped by
    // this same window's message loop (it was created on this thread), no
    // Dispatcher.Invoke needed here unlike LaunchSession's tray calls
    // (which originate from a background Task.Run).
    private void RestoreFromTray()
    {
        _windowTray.Hide();
        // Show() must come before WindowState=Normal - the OS still
        // considers the underlying HWND minimized (iconic) from the real
        // minimize that got us here, and setting WindowState first while
        // Visibility is still Hidden left it in a stuck state: a taskbar
        // button reappeared, but the window itself stayed minimized until
        // manually clicked. Show() first, then un-minimize, then focus.
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private string GetConfigLanguage() =>
        _configService.Get("_language") is JsonValue v && v.TryGetValue<string>(out var lang)
            ? lang
            : "EN";

    // Port of flightops_hub.pyw's width/height/x/y kwargs to webview.create_window
    // and api.save_window_geometry - ConfigManager already loads/migrates/
    // clamps these fields (see MigrateGeometry/ClampWindowPosition), this
    // was just never wired up on the WPF side yet. _window_x/_window_y
    // absent (never saved, or clamped away as bogus) means "let WPF center
    // it" - same as pywebview's x=None/y=None behavior.
    private void RestoreWindowGeometry()
    {
        if (GetConfigInt("_window_width") is { } width) Width = width;
        if (GetConfigInt("_window_height") is { } height) Height = height;

        var x = GetConfigInt("_window_x");
        var y = GetConfigInt("_window_y");
        if (x is not null && y is not null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = x.Value;
            Top = y.Value;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void SaveWindowGeometry()
    {
        // RestoreBounds, not the live Width/Height/Left/Top, when
        // maximized - those report the full-screen size while maximized,
        // and persisting that would reopen the window full-screen-sized
        // even after the user un-maximizes next time, possibly on a
        // different monitor layout. Python has no such distinction to
        // make (pywebview always reports the current, possibly-maximized
        // size) - a deliberate small improvement, not a divergence bug.
        var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        _configService.MutateAndSave(config =>
        {
            config["_window_width"] = (int)bounds.Width;
            config["_window_height"] = (int)bounds.Height;
            config["_window_x"] = (int)bounds.X;
            config["_window_y"] = (int)bounds.Y;
        });
    }

    private int? GetConfigInt(string key) =>
        _configService.Get(key) is JsonValue v && v.TryGetValue<int>(out var result) ? result : null;
}
