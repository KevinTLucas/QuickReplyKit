using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Interop;
using QuickReplyKit.Services;
using QuickReplyKit.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace QuickReplyKit;

public partial class App : Application
{
    private const string MutexName = "QuickReplyKit.SingleInstance.v1";
    private const string ShowEventName = "QuickReplyKit.Show.v1";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private Forms.NotifyIcon? _tray;
    private LauncherWindow? _launcher;
    private GlobalHotkey? _hotkey;
    private ForegroundMonitor? _foregroundMonitor;

    private AppConfig _config = new();
    private LibraryService? _library;
    private LauncherViewModel? _vm;
    private readonly ForegroundTargetTracker _target = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Hidden E2E gates: run a self-test and exit before touching tray/single-instance.
        if (e.Args.Any(a => a.Equals("--selftest-paste", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(SelfTest.Run());
            return;
        }
        if (e.Args.Any(a => a.Equals("--selftest-foreground", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(SelfTest.RunForeground());
            return;
        }
        if (e.Args.Any(a => a.Equals("--selftest-paste-rich", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(SelfTest.RunRichPaste());
            return;
        }
        if (e.Args.Any(a => a.Equals("--selftest-paste-ui", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(SelfTest.RunUiPaste());
            return;
        }

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        if (!isFirst)
        {
            // Another instance is already running: ask it to surface the launcher, then exit.
            _showEvent.Set();
            Shutdown(0);
            return;
        }

        _config = AppConfig.Load();

        // Debug/QA overrides (used by tools/shoot.ps1): --library <path>, --size WxH, --theme Light|Dark|System.
        string? libOverride = ArgValue(e.Args, "--library");
        if (!string.IsNullOrEmpty(libOverride)) _config.LibraryPath = libOverride;

        string? themeOverride = ArgValue(e.Args, "--theme");
        if (Enum.TryParse<AppTheme>(themeOverride, ignoreCase: true, out var themePref)) _config.Theme = themePref;

        ThemeManager.Apply(_config.Theme);

        _library = new LibraryService(_config.LibraryPath,
            seedSourceDir: Path.Combine(AppContext.BaseDirectory, "DefaultLibrary"));
        _vm = new LauncherViewModel(_library, new InsertService(_target), _config);

        // The watcher fires on a thread-pool thread; marshal the refresh to the UI thread.
        _library.Updated += () => Dispatcher.BeginInvoke(new Action(_vm.OnLibraryUpdated));

        InitTray();

        _launcher = new LauncherWindow(_vm, _config);
        string? sizeOverride = ArgValue(e.Args, "--size");
        if (!string.IsNullOrEmpty(sizeOverride)) _launcher.SuppressBoundsRestore = true;
        ApplySizeOverride(_launcher, sizeOverride);

        InitHotkey();

        // Track the last real (non-QuickReplyKit) foreground window so paste always targets the app
        // the user was actually in — regardless of whether the launcher is summoned via hotkey, tray,
        // or a fresh launch. Installed on the UI thread (it owns the message loop the hook needs).
        _foregroundMonitor = new ForegroundMonitor(_target);

        // Background waiter: when any future instance signals, show the launcher on the UI thread.
        var t = new Thread(ShowEventLoop) { IsBackground = true, Name = "ShowEventWaiter" };
        t.Start();

        if (e.Args.Any(a => a.Equals("--show", StringComparison.OrdinalIgnoreCase)))
            ShowLauncher();
    }

    private void InitHotkey()
    {
        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += ShowLauncher;   // fires on the UI thread
        if (!_hotkey.Rebind(_config.HotkeyGesture))
            _tray?.ShowBalloonTip(5000, "QuickReplyKit",
                $"Couldn't register the hotkey “{_config.HotkeyGesture}” (it may be in use). " +
                "The app is running; open it from the tray.", Forms.ToolTipIcon.Warning);
    }

    private void ShowEventLoop()
    {
        while (true)
        {
            _showEvent!.WaitOne();
            Dispatcher.Invoke(ShowLauncher);
        }
    }

    private void ShowLauncher()
    {
        CaptureForegroundTarget();
        _launcher?.ShowLauncher();
    }

    /// <summary>
    /// Remember the window to paste into: the foreground window at the moment the launcher is
    /// summoned (hotkey, tray, or --show), as long as it isn't our own launcher.
    /// </summary>
    private void CaptureForegroundTarget()
    {
        nint fg = ForegroundWindow.Get();
        nint mine = _launcher is null ? 0 : new WindowInteropHelper(_launcher).Handle;
        if (fg != 0 && fg != mine) _target.Target = fg;
    }

    private void InitTray()
    {
        Drawing.Icon icon;
        try { icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? Drawing.SystemIcons.Application; }
        catch { icon = Drawing.SystemIcons.Application; }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open QuickReplyKit", null, (_, _) => Dispatcher.Invoke(ShowLauncher));
        menu.Items.Add("Open Response Library", null, (_, _) => OpenLibraryFolder());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Import…", null, (_, _) => Dispatcher.Invoke(() => { ShowLauncher(); _launcher?.ImportFlow(); }));
        menu.Items.Add("Export library…", null, (_, _) => Dispatcher.Invoke(() => { ShowLauncher(); _launcher?.ExportLibraryFlow(); }));
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(() => Shutdown(0)));

        _tray = new Forms.NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "QuickReplyKit",
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowLauncher);
    }

    private void OpenLibraryFolder()
    {
        try
        {
            Directory.CreateDirectory(_config.LibraryPath);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(_config.LibraryPath) { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    /// <summary>Open the modal Settings dialog, owned by the launcher when it's visible.</summary>
    public void OpenSettings()
    {
        if (_config is null || _hotkey is null || _library is null || _vm is null) return;
        Window? owner = _launcher is { IsVisible: true } ? _launcher : null;
        var dlg = new Views.SettingsWindow(_config, _hotkey, _library, () => _vm.OnLibraryUpdated())
        {
            Owner = owner,
        };
        if (owner is null) dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
    }

    private static string? ArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static void ApplySizeOverride(Window window, string? size)
    {
        if (string.IsNullOrEmpty(size)) return;
        var parts = size.Split('x', 'X');
        if (parts.Length == 2 && double.TryParse(parts[0], out var w) && double.TryParse(parts[1], out var h))
        {
            window.Width = w;
            window.Height = h;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _foregroundMonitor?.Dispose();
        _hotkey?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _library?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
