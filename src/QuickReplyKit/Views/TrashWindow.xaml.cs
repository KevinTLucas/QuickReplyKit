using System.Windows;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Views;

/// <summary>
/// The app-local Trash: restore a soft-deleted response or folder to its original place, delete one forever, or
/// empty the lot. Backed by <see cref="TrashService"/>; a Restore refreshes the live library.
/// </summary>
public partial class TrashWindow : Window
{
    private readonly string _libraryRoot;
    private readonly Action _onLibraryChanged;
    private readonly int _retentionDays;

    private sealed class Row
    {
        public required TrashedItem Item { get; init; }
        public string Name => $"{Item.Name}   ·   {(Item.Kind == TrashItemKind.Folder ? "Folder" : "Response")}";
        public string Sub => $"{Item.OriginalRelativePath}   ·   deleted {Item.DeletedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
    }

    public TrashWindow(string libraryRoot, int retentionDays, Action onLibraryChanged)
    {
        InitializeComponent();
        _libraryRoot = libraryRoot;
        _retentionDays = retentionDays;
        _onLibraryChanged = onLibraryChanged;
        RetentionHint.Text = retentionDays > 0
            ? $"Deleted responses and folders are kept here for {retentionDays} days, then removed automatically."
            : "Deleted responses and folders are kept here until you remove them.";
        Refresh();
    }

    private void Refresh()
    {
        var rows = TrashService.List(AppPaths.TrashDir).Select(i => new Row { Item = i }).ToList();
        TrashList.ItemsSource = rows;
        if (rows.Count > 0) TrashList.SelectedIndex = 0;
        EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreButton.IsEnabled = DeleteButton.IsEnabled = rows.Count > 0;
        EmptyButton.IsEnabled = rows.Count > 0;
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (TrashList.SelectedItem is not Row row) return;
        var result = TrashService.RestoreDetailed(row.Item.Id, _libraryRoot, AppPaths.TrashDir);
        if (result == TrashRestoreResult.Restored)
        {
            _onLibraryChanged();
            Refresh();
        }
        else if (result == TrashRestoreResult.Conflict)
        {
            ConfirmDialog.Info(this, "Can't restore",
                $"A file or folder already exists at “{row.Item.OriginalRelativePath}”. Rename or move it, then try again. The Trash copy was kept.");
        }
        else ConfirmDialog.Info(this, "Can't restore",
            "This Trash item is missing or unsafe. Nothing was written to the response library, and the Trash copy was kept.");
    }

    private void DeleteForever_Click(object sender, RoutedEventArgs e)
    {
        if (TrashList.SelectedItem is not Row row) return;
        bool ok = ConfirmDialog.Confirm(this, "Delete forever",
            $"Permanently delete “{row.Item.Name}”? This can't be undone.", "Delete forever", destructive: true);
        if (!ok) return;
        if (!TrashService.DeleteForever(row.Item.Id, AppPaths.TrashDir))
        {
            ConfirmDialog.Info(this, "Can't delete", "This Trash item is unsafe or no longer exists.");
            return;
        }
        Refresh();
    }

    private void Empty_Click(object sender, RoutedEventArgs e)
    {
        bool ok = ConfirmDialog.Confirm(this, "Empty trash",
            "Permanently delete everything in the Trash? This can't be undone.", "Empty trash", destructive: true);
        if (!ok) return;
        TrashService.Empty(AppPaths.TrashDir);
        Refresh();
    }
}
