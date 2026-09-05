using System.IO;
using System.Threading;
using System.Windows;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Storage;
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

    private AppConfig _config = new();
    private LibraryService? _library;
    private LauncherViewModel? _vm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        // Debug/QA overrides (used by tools/shoot.ps1): --library <path>, --size WxH.
        string? libOverride = ArgValue(e.Args, "--library");
        if (!string.IsNullOrEmpty(libOverride)) _config.LibraryPath = libOverride;

        _library = new LibraryService(_config.LibraryPath,
            seedSourceDir: Path.Combine(AppContext.BaseDirectory, "DefaultLibrary"));
        _vm = new LauncherViewModel(_library, new ClipboardInsertService(), _config);

        // The watcher fires on a thread-pool thread; marshal the refresh to the UI thread.
        _library.Updated += () => Dispatcher.BeginInvoke(new Action(_vm.OnLibraryUpdated));

        InitTray();

        _launcher = new LauncherWindow(_vm, _config);
        ApplySizeOverride(_launcher, ArgValue(e.Args, "--size"));

        // Background waiter: when any future instance signals, show the launcher on the UI thread.
        var t = new Thread(ShowEventLoop) { IsBackground = true, Name = "ShowEventWaiter" };
        t.Start();

        if (e.Args.Any(a => a.Equals("--show", StringComparison.OrdinalIgnoreCase)))
            ShowLauncher();
    }

    private void ShowEventLoop()
    {
        while (true)
        {
            _showEvent!.WaitOne();
            Dispatcher.Invoke(ShowLauncher);
        }
    }

    private void ShowLauncher() => _launcher?.ShowLauncher();

    private void InitTray()
    {
        Drawing.Icon icon;
        try { icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? Drawing.SystemIcons.Application; }
        catch { icon = Drawing.SystemIcons.Application; }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open QuickReplyKit", null, (_, _) => Dispatcher.Invoke(ShowLauncher));
        menu.Items.Add("Open Response Library", null, (_, _) => OpenLibraryFolder());
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

    private void OpenSettings() => ShowLauncher();   // dedicated Settings window arrives in a later phase

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
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _library?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
