using System.IO;
using System.Windows;
using QuickReplyKit.Core;
using QuickReplyKit.Core.ImportExport;

namespace QuickReplyKit.Views;

/// <summary>
/// Back up / restore the response library from rolling snapshots (<see cref="LibraryBackup"/>).
/// Restore is confirmed and destructive, but snapshots the current state first so it is undoable.
/// </summary>
public partial class BackupWindow : Window
{
    private readonly string _libraryRoot;
    private readonly Core.LibraryService _library;
    private readonly Action _onLibraryChanged;

    private sealed class Row
    {
        public required BackupInfo Info { get; init; }
        public string Display => $"{Info.Created:yyyy-MM-dd HH:mm:ss}   ·   {Info.Bytes / 1024.0:0.#} KB";
    }

    public BackupWindow(string libraryRoot, Core.LibraryService library, Action onLibraryChanged)
    {
        InitializeComponent();
        _libraryRoot = libraryRoot;
        _library = library;
        _onLibraryChanged = onLibraryChanged;
        Refresh();
    }

    private void Refresh()
    {
        var rows = LibraryBackup.List(AppPaths.BackupsDir).Select(b => new Row { Info = b }).ToList();
        BackupList.ItemsSource = rows;
        if (rows.Count > 0) BackupList.SelectedIndex = 0;
        EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreButton.IsEnabled = rows.Count > 0;
    }

    private void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        var path = LibraryBackup.Create(_libraryRoot, AppPaths.BackupsDir, "manual");
        if (path is null)
            ConfirmDialog.Info(this, "Nothing to back up", "Your library is empty — no snapshot was taken.");
        Refresh();
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedItem is not Row row) return;
        bool ok = ConfirmDialog.Confirm(this, "Restore library",
            $"Replace your current library with the snapshot from {row.Info.Created:yyyy-MM-dd HH:mm:ss}?\n" +
            "Your current library is snapshotted first, so you can undo this.",
            "Restore", destructive: true);
        if (!ok) return;

        try
        {
            LibraryBackup.Restore(row.Info.Path, _libraryRoot, AppPaths.BackupsDir);
            _library.Reload();
            _onLibraryChanged();
            Refresh();
            ConfirmDialog.Info(this, "Library restored", "The library was restored from the snapshot.");
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
            Directory.CreateDirectory(AppPaths.BackupsDir);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(AppPaths.BackupsDir) { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }
}
