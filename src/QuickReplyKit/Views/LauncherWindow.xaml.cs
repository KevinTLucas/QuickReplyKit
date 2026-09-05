using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Core.Text;
using QuickReplyKit.ViewModels;
using QuickReplyKit.Views;

namespace QuickReplyKit;

public partial class LauncherWindow : Window
{
    private readonly LauncherViewModel _vm;
    private readonly AppConfig _config;

    private const double NavAutoCollapseWidth = 620;
    private double _navWidth = 180;   // remembered so collapse/expand restores the user's size
    private bool _navVisible = true;
    private bool _navUserOverride;    // once toggled by hand, stop auto-managing on resize

    public LauncherWindow(LauncherViewModel vm, AppConfig config)
    {
        _vm = vm;
        _config = config;
        InitializeComponent();
        DataContext = _vm;
        _vm.RequestHide += Hide;
    }

    /// <summary>Bring the launcher up, cleared and focused on the search box, ready to type.</summary>
    public void ShowLauncher()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        _vm.Query = "";
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // --- Keyboard contract --------------------------------------------------

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _vm.MoveSelection(1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;
            case Key.Up:
                _vm.MoveSelection(-1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;
            case Key.Enter:
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _vm.TriggerPlainInsert();
                else _vm.TriggerDefaultInsert();
                e.Handled = true;
                break;
            case Key.C when (Keyboard.Modifiers & ModifierKeys.Control) != 0 && SearchBox.SelectionLength == 0:
                // No text selected in the box → Ctrl+C copies the selected response instead.
                if (_vm.CopyCommand.CanExecute(null)) _vm.CopyCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; return; }

        // Ctrl+L / Ctrl+F refocus the search box (nice-to-have).
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (e.Key == Key.L || e.Key == Key.F))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void ScrollSelectedIntoView()
    {
        if (_vm.SelectedResult != null) ResultsList.ScrollIntoView(_vm.SelectedResult);
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only a double-click on an actual row inserts (not empty space or the scrollbar).
        if (ItemsControl.ContainerFromElement(ResultsList, e.OriginalSource as DependencyObject) is ListBoxItem)
            _vm.TriggerDefaultInsert();
    }

    private void NavTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is NavNodeVm node) _vm.SelectedNav = node;
    }

    // --- Nav collapse -------------------------------------------------------

    private void NavToggle_Click(object sender, RoutedEventArgs e)
    {
        _navUserOverride = true;
        SetNavVisible(!_navVisible);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_navUserOverride) return;
        bool shouldShow = ActualWidth >= NavAutoCollapseWidth;
        if (shouldShow != _navVisible) SetNavVisible(shouldShow);
    }

    private void SetNavVisible(bool visible)
    {
        if (!visible && NavColumn.ActualWidth > 0) _navWidth = NavColumn.ActualWidth;
        _navVisible = visible;
        NavColumn.MinWidth = visible ? 140 : 0;
        NavColumn.Width = visible ? new GridLength(_navWidth) : new GridLength(0);
        NavBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        NavSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Response management: create ----------------------------------------

    private void NewButton_Click(object sender, RoutedEventArgs e) =>
        OpenEditorForNew("");

    private void NewFromClipboardButton_Click(object sender, RoutedEventArgs e) =>
        OpenEditorForNew(ReadClipboardAsHtml());

    private void OpenEditorForNew(string html)
    {
        EditorWindow.OpenNew(this, _vm.LibraryRoot, _vm.CategoryPaths(), _vm.CurrentCategory, html);
        // On save the file watcher refreshes the library; nothing else to do here.
    }

    /// <summary>Read the clipboard into subset HTML: prefer HTML, fall back to wrapped plain text.</summary>
    private static string ReadClipboardAsHtml()
    {
        try
        {
            if (Clipboard.ContainsData(DataFormats.Html))
                return ClipboardHtml.ExtractFragment(Clipboard.GetData(DataFormats.Html) as string);
            if (Clipboard.ContainsText())
                return ClipboardHtml.FromPlainText(Clipboard.GetText());
        }
        catch { /* clipboard busy/locked — start empty */ }
        return "";
    }

    // --- Results context menu -----------------------------------------------

    // Right-click selects the row under the cursor first, so the menu acts on it.
    private void ResultsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { } row)
            row.IsSelected = true;
    }

    private void ResultsContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var item = _vm.SelectedItem;
        // No selection → suppress the menu entirely.
        if (item == null) { ResultsContextMenu.IsOpen = false; return; }

        // Edit and Copy only make sense for a valid, readable response.
        MenuEdit.IsEnabled = item.IsValid;
        MenuCopy.IsEnabled = item.IsValid;
        MenuDuplicate.IsEnabled = true;
    }

    private void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is { IsValid: true } item)
            EditorWindow.OpenEdit(this, _vm.LibraryRoot, _vm.CategoryPaths(), item);
    }

    private void MenuDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is { } item) ResponseManager.Duplicate(item);
    }

    private void MenuRename_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is not { } item) return;
        string? name = InputDialog.Prompt(this, "Rename response", "New name:", item.Name);
        if (name == null) return;
        ResolveAndRun(n => ResponseManager.Rename(item, n), name);
    }

    private void MenuMove_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is not { } item) return;
        string? category = InputDialog.Prompt(this, "Move to…",
            "Destination folder (type a new path to create it):",
            item.CategoryPath, _vm.CategoryPaths());
        if (category == null) return;

        var res = ResponseManager.Move(item, category);
        while (res.Kind == ResultKind.Conflict)
        {
            bool overwrite = ConfirmDialog.Confirm(this, "Name already exists",
                $"“{item.Name}” already exists in that folder. Overwrite it?", "Overwrite", destructive: true);
            if (!overwrite) return;
            Recycle(res.Existing);
            res = ResponseManager.Move(item, category);
        }
        if (res.Kind == ResultKind.InvalidName)
            ConfirmDialog.Confirm(this, "Invalid name", res.Error ?? "That name can't be used.", "OK");
    }

    private void MenuCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.CopyCommand.CanExecute(null)) _vm.CopyCommand.Execute(null);
    }

    private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is not { } item || string.IsNullOrEmpty(item.FullPath)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"") { UseShellExecute = true }); }
        catch { /* best effort */ }
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is not { } item) return;
        bool ok = ConfirmDialog.Confirm(this, "Delete response",
            $"Delete “{item.Name}”?\nIt will be moved to the Recycle Bin, so you can restore it later.",
            "Delete", destructive: true);
        if (ok) ResponseManager.Delete(item);
    }

    // --- Conflict resolution shared by rename ------------------------------

    private void ResolveAndRun(System.Func<string, ManageResult> op, string name)
    {
        while (true)
        {
            var res = op(name);
            switch (res.Kind)
            {
                case ResultKind.Ok:
                    return;
                case ResultKind.InvalidName:
                    ConfirmDialog.Confirm(this, "Invalid name",
                        res.Error ?? "That name can't be used.", "OK");
                    return;
                case ResultKind.Conflict:
                    switch (ConfirmDialog.AskConflict(this, name))
                    {
                        case ConflictChoice.Overwrite: Recycle(res.Existing); break;   // retry
                        case ConflictChoice.KeepBoth: name = BumpName(name); break;
                        default: return;
                    }
                    break;
            }
        }
    }

    private static string BumpName(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.*?) \((\d+)\)$");
        return m.Success
            ? $"{m.Groups[1].Value} ({int.Parse(m.Groups[2].Value) + 1})"
            : $"{name} (2)";
    }

    private static void Recycle(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null && node is not T)
            node = VisualTreeHelper.GetParent(node);
        return node as T;
    }

    // --- Settings gear (stub: opens the library folder for now) -------------

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_config.LibraryPath);
            Process.Start(new ProcessStartInfo(_config.LibraryPath) { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    // Closing hides to tray instead of exiting; the app exits explicitly via the tray menu.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
