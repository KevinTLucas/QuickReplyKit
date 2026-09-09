using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.Model;
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
    private HotkeyService? _hotkeys;
    private ForegroundMonitor? _foregroundMonitor;
    private bool _restartAfterExit;

    private AppConfig _config = new();
    private LibraryService? _library;
    private LauncherViewModel? _vm;
    private readonly ForegroundTargetTracker _target = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Hidden E2E gates: run a self-test and exit before touching tray/single-instance.
        string? selfTestTarget = ArgValue(e.Args, "--selftest-target");
        if (!string.IsNullOrEmpty(selfTestTarget))
        {
            Shutdown(SelfTest.RunTarget(selfTestTarget));
            return;
        }
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

        // Debug/QA overrides (used by tools/shoot.ps1): --library <path>, --size WxH, --theme Light|Dark|System,
        // --show-editor (pop the New-response editor for theming screenshots).
        string? libOverride = ArgValue(e.Args, "--library");
        if (!string.IsNullOrEmpty(libOverride)) _config.LibraryPath = libOverride;

        string? themeOverride = ArgValue(e.Args, "--theme");
        if (Enum.TryParse<AppTheme>(themeOverride, ignoreCase: true, out var themePref)) _config.Theme = themePref;

        InitLogging();

        ThemeManager.Apply(_config.Theme);

        // Defer the library scan off the UI thread: with a large library it can take many seconds
        // (I/O-bound), and doing it here would stall the launcher from appearing. The VM starts in a
        // "Loading…" state and populates when LoadInitial() (kicked off at the end of startup) fires
        // Updated on the Dispatcher — the same path the watcher already uses.
        _library = new LibraryService(_config.LibraryPath,
            seedSourceDir: Path.Combine(AppContext.BaseDirectory, "DefaultLibrary"),
            deferInitialScan: true);
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

        // QA override: pop the editor (New) directly so its theming can be screenshotted. The
        // sample categories give the Category combobox dropdown some items to render.
        if (e.Args.Any(a => a.Equals("--show-editor", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _launcher!.Show();
                Views.EditorWindow.OpenNew(
                    _launcher, _config!.LibraryPath, new[] { "IXL", "IXL/Math", "Billing", "Onboarding" }, "IXL");
            }));

        if (e.Args.Any(a => a.Equals("--show-about", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = new Views.AboutWindow(_config!, _library!, _hotkeys!);
                if (_launcher is { IsVisible: true }) dialog.Owner = _launcher;
                else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.ShowDialog();
            }));

        if (e.Args.Any(a => a.Equals("--show-backup", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = new Views.BackupWindow(
                    _config!.LibraryPath, _config.BackupPath, _library!, () => _vm!.OnLibraryUpdated());
                if (_launcher is { IsVisible: true }) dialog.Owner = _launcher;
                else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.ShowDialog();
            }));

        if (e.Args.Any(a => a.Equals("--show-onboarding", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(new Action(() => new Views.OnboardingWindow(_config.HotkeyGesture).ShowDialog()));

        // First-run intro (once): show the short onboarding, then remember it so it never pops again.
        if (!_config.OnboardingSeen)
        {
            _config.OnboardingSeen = true;
            _config.Save();
            Dispatcher.BeginInvoke(new Action(() => new Views.OnboardingWindow(_config.HotkeyGesture).ShowDialog()));
        }

        if (e.Args.Any(a => a.Equals("--show-trash", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _launcher!.Show();
                new Views.TrashWindow(_config!.LibraryPath, _config.TrashRetentionDays,
                    () => _vm!.ReloadLibrary()) { Owner = _launcher }.ShowDialog();
            }));

        // Auto-purge expired Trash in the background (retention 0 = keep forever).
        if (_config.TrashRetentionDays > 0)
            Task.Run(() =>
            {
                try
                {
                    int n = TrashService.Purge(AppPaths.TrashDir, TimeSpan.FromDays(_config.TrashRetentionDays));
                    if (n > 0) Diag.Info("App", "Trash", $"auto-purge complete removed={n}");
                }
                catch { /* best effort */ }
            });

        // QA override: pop the keyboard-shortcuts manager so its theming can be screenshotted.
        if (e.Args.Any(a => a.Equals("--show-hotkeys", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(new Action(OpenHotkeyManager));

        // Run the initial library scan in the background; it populates the (already-visible) launcher
        // via Updated → Dispatcher. The window is interactive immediately, showing "Loading library…".
        Task.Run(() => _library.LoadInitial());
    }

    /// <summary>App version from the assembly (single-file-safe), else "dev".</summary>
    public static string Version =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";

    // Start the rolling logger and route every crash channel into it, so "send me the log file" is
    // enough to diagnose an issue. Crashes are LOGGED but not swallowed — surfacing a fault beats
    // silently limping on in a possibly-corrupt state (the fix is to fix the fault, not hide it).
    private void InitLogging()
    {
        Diag.Init(AppPaths.LogsDir, _config.VerboseLogging);
        Diag.Info("App", "Lifecycle",
            $"session start version={Version} os={Environment.OSVersion.Version} theme={_config.Theme} verbose={_config.VerboseLogging}");

        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            Diag.Error("App", "Crash", $"unhandled AppDomain exception terminating={ev.IsTerminating}", ev.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, ev) =>
            Diag.Error("App", "Crash", "unhandled Dispatcher exception", ev.Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) =>
            { Diag.Error("App", "Crash", "unobserved task exception", ev.Exception); ev.SetObserved(); };
        Exit += (_, _) => { Diag.Info("App", "Lifecycle", "session end"); Diag.Shutdown(); };
    }

    private void InitHotkey()
    {
        _hotkeys = new HotkeyService(_config, _library!, new InsertService(_target));
        _hotkeys.LauncherPressed += ShowLauncher;   // fires on the UI thread
        _hotkeys.ScriptFailed += message => Dispatcher.BeginInvoke(new Action(() =>
        {
            string text = message + " Nothing was pasted. Open Script Manager for details.";
            if (text.Length > 240) text = text[..237] + "...";
            _tray?.ShowBalloonTip(7000, "Response could not be prepared", text, Forms.ToolTipIcon.Error);
        }));
        _hotkeys.ReloadAll();                        // launcher + every per-response instant-paste bind
        if (!_hotkeys.LauncherRegistered)
            _tray?.ShowBalloonTip(5000, "QuickReplyKit",
                $"Couldn't register the hotkey “{_config.HotkeyGesture}” (it may be in use). " +
                "The app is running; open it from the tray.", Forms.ToolTipIcon.Warning);
    }

    /// <summary>Set or replace the instant-paste bind for a response, then persist and re-register.</summary>
    public void SetResponseHotkey(string relativePath, string gesture, bool plain)
    {
        _config.ResponseHotkeys.RemoveAll(b =>
            string.Equals(b.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        _config.ResponseHotkeys.Add(new ResponseHotkey { RelativePath = relativePath, Gesture = gesture, Plain = plain });
        PersistHotkeys();
    }

    /// <summary>Remove any instant-paste bind for a response, then persist and re-register.</summary>
    public void ClearResponseHotkey(string relativePath)
    {
        if (_config.ResponseHotkeys.RemoveAll(b =>
                string.Equals(b.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)) > 0)
            PersistHotkeys();
    }

    private void PersistHotkeys()
    {
        try { _config.Save(); } catch { /* best effort */ }
        _hotkeys?.ReloadAll();
    }

    /// <summary>The current bind for a response's relative path, or null.</summary>
    public ResponseHotkey? ResponseHotkeyFor(string relativePath) =>
        _config.ResponseHotkeys.FirstOrDefault(b =>
            string.Equals(b.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Open the "Assign hotkey…" dialog for a response and apply the chosen bind.</summary>
    public void OpenAssignHotkey(ResponseItem item)
    {
        if (_hotkeys is null) return;
        Window? owner = _launcher is { IsVisible: true } ? _launcher : null;
        var existing = ResponseHotkeyFor(item.RelativePath);
        var dlg = new Views.AssignHotkeyWindow(item.Name, existing,
            gesture => DescribeConflict(gesture, item.RelativePath))
        {
            Owner = owner,
        };
        if (owner is null) dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (dlg.ShowDialog() != true) return;

        if (dlg.Removed) ClearResponseHotkey(item.RelativePath);
        else SetResponseHotkey(item.RelativePath, dlg.Gesture!, dlg.Plain);
    }

    /// <summary>Open the keyboard-shortcuts manager (launcher + all binds, conflicts, search-by-key).</summary>
    public void OpenHotkeyManager()
    {
        if (_hotkeys is null || _library is null) return;
        Window? owner = _launcher is { IsVisible: true } ? _launcher : null;
        var dlg = new Views.HotkeyManagerWindow(_config, _hotkeys, _library, this) { Owner = owner };
        if (owner is null) dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
    }

    /// <summary>
    /// Human conflict message if <paramref name="gesture"/> is already owned by the launcher or a
    /// different response (excluding <paramref name="selfPath"/>), else null (free to assign).
    /// </summary>
    public string? DescribeConflict(string gesture, string? selfPath)
    {
        var others = _config.ResponseHotkeys.Where(b =>
            !string.Equals(b.RelativePath, selfPath, StringComparison.OrdinalIgnoreCase));
        var hit = HotkeyConflicts.Resolve(gesture, _config.HotkeyGesture, others);
        return hit.Kind switch
        {
            HotkeyConflicts.TargetKind.Launcher => "Already used by the launcher hotkey.",
            HotkeyConflicts.TargetKind.Response =>
                $"Already used by “{Path.GetFileNameWithoutExtension(hit.RelativePath)}”.",
            _ => null,
        };
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
        Diag.Verbose("App", "Action", "show launcher");
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
        menu.Items.Add("Open logs folder", null, (_, _) => OpenLogsFolder());
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

    /// <summary>Open the logs folder in Explorer (creating it first so it always opens).</summary>
    public static void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(AppPaths.LogsDir) { UseShellExecute = true });
        }
        catch (Exception ex) { Diag.Warn("App", "Action", "open logs folder failed", ex); }
    }

    /// <summary>Open the modal Settings dialog, owned by the launcher when it's visible.</summary>
    public void OpenSettings()
    {
        if (_config is null || _hotkeys is null || _library is null || _vm is null) return;
        Window? owner = _launcher is { IsVisible: true } ? _launcher : null;
        var dlg = new Views.SettingsWindow(_config, _hotkeys, _library, () => _vm.OnLibraryUpdated())
        {
            Owner = owner,
        };
        if (owner is null) dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
    }

    /// <summary>Restart after a backup restore so the restored config becomes the live state.</summary>
    public void RestartAfterRestore()
    {
        _restartAfterExit = true;
        if (_launcher is not null) _launcher.SuppressBoundsSave = true;
        Shutdown(0);
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
        string? restart = _restartAfterExit ? Environment.ProcessPath : null;
        _foregroundMonitor?.Dispose();
        _hotkeys?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _library?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
        if (restart is not null)
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(restart, "--show") { UseShellExecute = true }); }
            catch (Exception ex) { Diag.Error("App", "Action", "restart after restore failed", ex); }
    }
}
