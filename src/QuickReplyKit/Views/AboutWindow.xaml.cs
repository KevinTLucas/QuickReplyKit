using System.IO;
using System.Windows;
using System.Windows.Input;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Services;

namespace QuickReplyKit.Views;

/// <summary>
/// Read-only About / Diagnostics screen: app version, paths, library/hotkey/watcher state, and OS
/// runtime — everything a support request needs in one copyable block ("screenshot this page").
/// </summary>
public partial class AboutWindow : Window
{
    private readonly string _library;
    private readonly string _backup;

    public AboutWindow(AppConfig config, LibraryService library, HotkeyService hotkeys)
    {
        InitializeComponent();
        void LimitHeight() => MaxHeight = Math.Max(MinHeight, System.Windows.Forms.Screen
            .FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea.Height
            / System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleY);
        Loaded += (_, _) => { LimitHeight(); LocationChanged += (_, _) => LimitHeight(); };
        _library = config.LibraryPath;
        _backup = config.BackupPath;
        DiagText.Text = DiagnosticsReport.Build(config, library, hotkeys.Entries, App.Version);
        TagLine.Text = $"Response launcher · v{App.Version}";
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => App.OpenLogsFolder();
    private void OpenConfig_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.AppDataDir);
    private void OpenLibrary_Click(object sender, RoutedEventArgs e) => OpenFolder(_library);
    private void OpenBackup_Click(object sender, RoutedEventArgs e) => OpenFolder(_backup);
    private void OpenTrash_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.TrashDir);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }

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
        catch (Exception ex) { Diag.Warn("AboutWindow", "Action", "open folder failed", ex); }
    }
}
