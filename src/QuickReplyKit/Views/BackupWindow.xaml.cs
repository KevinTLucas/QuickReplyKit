using System.IO;
using System.Windows;
using QuickReplyKit.Core;
using QuickReplyKit.Core.ImportExport;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Views;

/// <summary>
/// Back up / restore the response library from rolling snapshots (<see cref="LibraryBackup"/>).
/// Restore is confirmed and destructive, but snapshots the current state first so it is undoable.
/// </summary>
public partial class BackupWindow : Window
{
    private readonly string _libraryRoot;
    private readonly string _backupsDir;
    private readonly Core.LibraryService _library;
    private readonly Action _onLibraryChanged;

    private sealed class Row
    {
        public required BackupInfo Info { get; init; }
        public string Display => $"{Info.Created:yyyy-MM-dd HH:mm:ss}   ·   {Info.Bytes / 1024.0:0.#} KB";
    }

    public BackupWindow(string libraryRoot, string backupsDir, Core.LibraryService library, Action onLibraryChanged)
    {
        InitializeComponent();
        _libraryRoot = libraryRoot;
        _backupsDir = backupsDir;
        _library = library;
        _onLibraryChanged = onLibraryChanged;
        Refresh();
    }

    private void Refresh()
    {
        List<Row> rows;
        try
        {
            rows = LibraryBackup.List(_backupsDir).Select(b => new Row { Info = b }).ToList();
            EmptyHint.Text = "No snapshots yet.";
        }
        catch (Exception ex)
        {
            rows = new();
            EmptyHint.Text = "Backup folder unavailable: " + ex.Message;
        }
        BackupList.ItemsSource = rows;
        if (rows.Count > 0) BackupList.SelectedIndex = 0;
        EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreButton.IsEnabled = rows.Count > 0;
    }

    private void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = LibraryBackup.Create(_libraryRoot, _backupsDir, "manual");
            if (path is null)
                ConfirmDialog.Info(this, "Nothing to back up", "Your library is empty — no snapshot was taken.");
        }
        catch (Exception ex)
        {
            ConfirmDialog.Info(this, "Backup failed", "Couldn't create a snapshot:\n" + ex.Message);
        }
        Refresh();
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedItem is not Row row) return;
        bool ok = ConfirmDialog.Confirm(this, "Restore library",
            $"Replace your current library with the snapshot from {row.Info.Created:yyyy-MM-dd HH:mm:ss}?\n" +
            "Your current library is snapshotted first, so you can undo this.\n\n" +
            "Modern snapshots also replace local settings and restart QuickReplyKit. " +
            "The restored library stays at its current location. Legacy snapshots replace only the library.",
            "Restore", destructive: true);
        if (!ok) return;

        try
        {
            bool settingsRestored = LibraryBackup.Restore(row.Info.Path, _libraryRoot, _backupsDir);
            _library.Reload();
            _onLibraryChanged();
            Refresh();
            if (settingsRestored)
            {
                ConfirmDialog.Info(this, "Library restored",
                    "The library and settings were restored. QuickReplyKit will restart to apply them.");
                (Application.Current as App)?.RestartAfterRestore();
            }
            else
                ConfirmDialog.Info(this, "Library restored", "The library was restored from the legacy snapshot.");
        }
        catch (Exception ex)
        {
            ConfirmDialog.Info(this, "Restore failed", "Couldn't restore that snapshot:\n" + ex.Message);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BackupLocation.EnsureWritable(_backupsDir);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(_backupsDir) { UseShellExecute = true });
        }
        catch (Exception ex) { ConfirmDialog.Info(this, "Backup folder unavailable", ex.Message); }
    }
}
