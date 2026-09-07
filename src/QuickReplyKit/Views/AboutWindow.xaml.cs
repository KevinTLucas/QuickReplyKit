using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Views;

/// <summary>
/// Read-only About / Diagnostics screen: app version, paths, library/hotkey/watcher state, and OS
/// runtime — everything a support request needs in one copyable block ("screenshot this page").
/// </summary>
public partial class AboutWindow : Window
{
    private readonly string _library;

    public AboutWindow(AppConfig config, LibraryService library)
    {
        InitializeComponent();
        void LimitHeight() => MaxHeight = Math.Max(MinHeight, System.Windows.Forms.Screen
            .FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea.Height
            / System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleY);
        Loaded += (_, _) => { LimitHeight(); LocationChanged += (_, _) => LimitHeight(); };
        _library = config.LibraryPath;

        var snap = library.Snapshot;
        int valid = 0, invalid = 0;
        foreach (var r in snap.AllResponses) { if (r.IsValid) valid++; else invalid++; }

        var sb = new StringBuilder();
        void Row(string k, string v) => sb.Append(k.PadRight(18)).Append(": ").AppendLine(v);
        Row("Version", App.Version);
        Row("Runtime", RuntimeInformation.FrameworkDescription);
        Row("OS", RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture + ")");
        sb.AppendLine();
        Row("Global hotkey", config.HotkeyGesture);
        Row("Default paste", config.DefaultPasteMode.ToString());
        Row("Theme", config.Theme.ToString());
        Row("Verbose logging", config.VerboseLogging ? "on" : "off");
        sb.AppendLine();
        Row("Library folder", config.LibraryPath);
        Row("Responses", $"{snap.AllResponses.Count} ({valid} valid, {invalid} invalid)");
        Row("Watching library", library.IsWatching ? "yes" : "no");
        sb.AppendLine();
        Row("Config file", AppPaths.ConfigPath);
        Row("Logs folder", AppPaths.LogsDir);

        DiagText.Text = sb.ToString();
        TagLine.Text = $"Response launcher · v{App.Version}";
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => App.OpenLogsFolder();
    private void OpenConfig_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.AppDataDir);
    private void OpenLibrary_Click(object sender, RoutedEventArgs e) => OpenFolder(_library);

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(DiagText.Text); } catch { /* clipboard busy */ }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Diag.Warn("About: open folder failed: " + ex.Message); }
    }
}
