using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using QuickReplyKit.Core;
using QuickReplyKit.Core.ImportExport;
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

    /// <summary>Set when a --size override is in effect (screenshots) so saved bounds don't fight it.</summary>
    public bool SuppressBoundsRestore { get; set; }

    /// <summary>Set during a config restore so shutdown cannot overwrite the restored file.</summary>
    public bool SuppressBoundsSave { get; set; }
    private bool _boundsRestored;     // restore once, on the first show

    public LauncherWindow(LauncherViewModel vm, AppConfig config)
    {
        _vm = vm;
        _config = config;
        InitializeComponent();
        DataContext = _vm;
        _vm.RequestHide += HideLauncher;
    }

    /// <summary>Bring the launcher up, cleared and focused on the search box, ready to type.</summary>
    public void ShowLauncher()
    {
        RestorePlacement();
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
            case Key.Back when SearchBox.Text.Length == 0 && _vm.HasSearchScope:
                _vm.ClearSearchScope();
                e.Handled = true;
                break;
        }
    }

    private void ClearSearchScope_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearSearchScope();
        SearchBox.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { HideLauncher(); e.Handled = true; return; }

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

    private async void NewFromClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        NewFromClipboardButton.IsEnabled = false;
        try
        {
            var (html, needsSanitizing) = ReadClipboardContent();
            if (html.Length > ClipboardHtml.MaxInputChars)
            {
                ConfirmDialog.Info(this, "Clipboard content is too large",
                    "QuickReplyKit can import up to 250,000 characters of clipboard content.");
                return;
            }
            string sanitized = needsSanitizing
                ? await Task.Run(() => ClipboardHtml.Sanitize(html))
                : ClipboardHtml.FromPlainText(html);
            if (html.Length > 0 && sanitized.Length == 0)
            {
                ConfirmDialog.Info(this, "Clipboard content wasn't imported",
                    "The clipboard content was too complex or could not be converted safely.");
                return;
            }
            OpenEditorForNew(sanitized);
        }
        finally { NewFromClipboardButton.IsEnabled = true; }
    }

    private void OpenEditorForNew(string html)
    {
        EditorWindow.OpenNew(this, _vm.LibraryRoot, _vm.CategoryPaths(), _vm.CurrentCategory, html);
        // On save the file watcher refreshes the library; nothing else to do here.
    }

    /// <summary>Read clipboard data on the UI thread; HTML normalization then runs in the background.</summary>
    private static (string Content, bool IsHtml) ReadClipboardContent()
    {
        try
        {
            if (Clipboard.ContainsData(DataFormats.Html))
                return (Clipboard.GetData(DataFormats.Html) as string ?? "", true);
            if (Clipboard.ContainsText())
                return (Clipboard.GetText(), false);
        }
        catch { /* clipboard busy/locked — start empty */ }
        return ("", false);
    }

    // --- Results context menu -----------------------------------------------

    // Right-click selects the row under the cursor first, so the menu acts on it.
    private void ResultsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { } row)
            row.IsSelected = true;
    }

    // Gate the menu BEFORE it opens: cancel it when nothing is selected, and set item
    // enablement here. (Never mutate IsOpen inside Opened — that reentrancy crashes on
    // fast repeated right-clicks. Cancelling via e.Handled is the safe WPF pattern.)
    private void ResultsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = _vm.SelectedItem;
        if (item == null) { e.Handled = true; return; }

        // Edit, Edit & insert, and Copy only make sense for a valid, readable response.
        MenuEdit.IsEnabled = item.IsValid;
        MenuEditInsert.IsEnabled = item.IsValid;
        MenuCopy.IsEnabled = item.IsValid;

        // "Clear" only shows when the response currently has an instant-paste bind.
        bool hasHotkey = (Application.Current as App)?.ResponseHotkeyFor(item.RelativePath) is not null;
        MenuAssignHotkey.IsEnabled = item.IsValid;
        MenuClearHotkey.Visibility = hasHotkey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MenuAssignHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is { IsValid: true } item)
            (Application.Current as App)?.OpenAssignHotkey(item);
    }

    private void MenuClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is { } item)
            (Application.Current as App)?.ClearResponseHotkey(item.RelativePath);
    }

    private void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is { IsValid: true } item)
            EditorWindow.OpenEdit(this, _vm.LibraryRoot, _vm.CategoryPaths(), item);
    }

    private void MenuEditInsert_Click(object sender, RoutedEventArgs e)
    {
        // Open the editor on the resolved message; on Insert the edited text is pasted, not saved.
        if (_vm.SelectedItem is { IsValid: true } item)
            EditorWindow.OpenCompose(this, _vm.ResolveComposed(item), _vm.InsertComposed);
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
        if (_vm.SelectedItem is not { } item || string.IsNullOrEmpty(item.FullPath)) return;
        bool ok = ConfirmDialog.Confirm(this, "Delete response",
            $"Delete “{item.Name}”?\nIt moves to the app's Trash — restore it any time from Settings → Trash.",
            "Delete", destructive: true);
        if (!ok) return;
        try
        {
            TrashService.Trash(item.FullPath, _vm.LibraryRoot, AppPaths.TrashDir);
            _vm.ReloadLibrary();
        }
        catch (Exception ex)
        {
            ConfirmDialog.Info(this, "Response not deleted", ex.Message);
        }
    }

    // --- Import / export ----------------------------------------------------

    // Right-click a nav node → select it so the category menu acts on it.
    private void NavTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { } node)
            node.IsSelected = true;
    }

    // Only categories can be exported as a folder; cancel the menu on Favorites/Recent/All.
    // Cancel via e.Handled before it opens — never toggle IsOpen inside Opened (crashes on fast repeats).
    private void NavTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_vm.SelectedNav?.Scope != NavScope.Category) e.Handled = true;
    }

    private void OverflowButton_Click(object sender, RoutedEventArgs e)
    {
        if (OverflowButton.ContextMenu is { } m) { m.PlacementTarget = OverflowButton; m.IsOpen = true; }
    }

    private void MenuExportResponse_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedItem is not { } item) return;
        var dlg = new SaveFileDialog
        {
            Title = "Export response",
            Filter = "Response JSON (*.json)|*.json",
            FileName = item.Name + ".json",
        };
        if (dlg.ShowDialog(this) == true) LibraryPorter.Export(item, dlg.FileName);
    }

    private void MenuExportFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNav is not { Scope: NavScope.Category } nav) return;
        var dlg = new SaveFileDialog
        {
            Title = "Export folder",
            Filter = "Zip archive (*.zip)|*.zip",
            FileName = nav.Title + ".zip",
        };
        if (dlg.ShowDialog(this) == true)
            SaveZip(tmp => LibraryPorter.ExportFolder(_vm.LibraryRoot, nav.CategoryPath, tmp), dlg.FileName);
    }

    private void MenuDeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNav is not { Scope: NavScope.Category } nav) return;
        int count = _vm.ResponseCountUnder(nav.CategoryPath);
        bool ok = ConfirmDialog.Confirm(this, "Delete folder",
            $"Delete folder “{nav.Title}” and its {count} nested response file(s), including valid and invalid responses?\n" +
            "The complete folder moves to the app's Trash after a safety backup is created.",
            "Delete folder", destructive: true);
        if (!ok) return;

        try
        {
            TrashService.TrashFolder(_vm.CategoryFullPath(nav.CategoryPath), _vm.LibraryRoot,
                AppPaths.TrashDir, _config.BackupPath);
            _vm.ReloadLibrary();
        }
        catch (Exception ex)
        {
            ConfirmDialog.Info(this, "Folder not deleted", ex.Message);
        }
    }

    private void MenuExportLibrary_Click(object sender, RoutedEventArgs e) => ExportLibraryFlow();
    private void MenuImport_Click(object sender, RoutedEventArgs e) => ImportFlow();

    /// <summary>Export the whole library to a chosen .zip (also reachable from the tray).</summary>
    public void ExportLibraryFlow()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export library",
            Filter = "Zip archive (*.zip)|*.zip",
            FileName = "QuickReplyKit Library.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dlg.ShowDialog(this) == true)
            SaveZip(tmp => LibraryPorter.ExportLibrary(_vm.LibraryRoot, tmp), dlg.FileName);
    }

    /// <summary>Import a .json/.zip: pick target category, resolve conflicts, then summarise
    /// (also reachable from the tray). The watcher refreshes the library on its own.</summary>
    public void ImportFlow()
    {
        var open = new OpenFileDialog
        {
            Title = "Import responses",
            Filter = "Responses (*.json;*.zip)|*.json;*.zip|Response JSON (*.json)|*.json|Zip archive (*.zip)|*.zip",
            CheckFileExists = true,
        };
        if (open.ShowDialog(this) != true) return;
        string source = open.FileName;

        string? target = InputDialog.Prompt(this, "Import into…",
            "Target folder (blank = top level; type a new path to create it):",
            _vm.CurrentCategory, _vm.CategoryPaths(), allowEmpty: true);
        if (target == null) return;

        // Plan once, review that exact immutable plan, then execute it. Preview and writes cannot drift.
        ImportPlan plan;
        try { plan = LibraryPorter.PlanImport(_vm.LibraryRoot, source, target); }
        catch (Exception ex)
        {
            ConfirmDialog.Info(this, "Can't read that file", "Import preview failed:\n" + ex.Message);
            return;
        }
        if (plan.Entries.Count == 0)
        {
            ConfirmDialog.Info(this, "Nothing to import", "No responses were found in that file.");
            return;
        }
        var decisions = ImportPreviewWindow.Prompt(this, plan);
        if (decisions == null) return;

        // A bulk write never starts unless the current library can be protected first.
        try { LibraryBackup.CreateRequired(_vm.LibraryRoot, _config.BackupPath, "before import"); }
        catch (IOException ex)
        {
            ConfirmDialog.Info(this, "Import canceled", ex.Message);
            return;
        }

        var result = LibraryPorter.Import(plan, decisions);
        ConfirmDialog.Info(this, "Import complete", SummaryText(result));
    }

    private static string SummaryText(ImportResult result)
    {
        int Count(params ImportOutcomeKind[] kinds) => result.Outcomes.Count(o => kinds.Contains(o.Kind));
        var parts = new List<string>();
        void Add(int count, string text) { if (count > 0) parts.Add($"{count} {text}"); }
        Add(Count(ImportOutcomeKind.Imported), "imported");
        Add(Count(ImportOutcomeKind.Overwritten), "overwritten");
        Add(Count(ImportOutcomeKind.KeptBoth), "kept as copies");
        Add(Count(ImportOutcomeKind.Skipped), "skipped");
        Add(Count(ImportOutcomeKind.Invalid, ImportOutcomeKind.Unsafe), "invalid or unsafe");
        Add(Count(ImportOutcomeKind.Stale), "changed after preview");
        Add(Count(ImportOutcomeKind.Failed), "failed");
        return parts.Count == 0 ? "No changes were made." : string.Join(", ", parts) + ".";
    }

    /// <summary>Write a zip via a temp file, then move it into place — never inside the zipped tree.</summary>
    private static void SaveZip(Action<string> export, string dest)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "QRK-Export-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            export(tmp);
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ } }
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

    internal static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T found) return found;
            node = node switch
            {
                ContentElement content => ContentOperations.GetParent(content)
                    ?? (content as FrameworkContentElement)?.Parent,
                Visual => VisualTreeHelper.GetParent(node),
                System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(node),
                _ => LogicalTreeHelper.GetParent(node),
            };
        }
        return null;
    }

    // --- Window memory (size + position, always reopened on-screen) ----------

    /// <summary>Hide to tray, first saving the launcher's placement so the next show restores it.</summary>
    private void HideLauncher()
    {
        SaveBounds();
        Hide();
    }

    /// <summary>Persist the current normal-state bounds (and the monitor it sits on) to config.</summary>
    private void SaveBounds()
    {
        if (SuppressBoundsSave || !_config.RememberWindow || WindowState != WindowState.Normal || !IsVisible) return;

        _config.Window = new WindowBounds
        {
            X = Left,
            Y = Top,
            Width = ActualWidth,
            Height = ActualHeight,
            MonitorDeviceName = ScreenDeviceNameAt(Left + ActualWidth / 2, Top + ActualHeight / 2),
        };
        try { _config.Save(); } catch { /* best effort */ }
    }

    /// <summary>
    /// On the first show, if remembering and real bounds are saved, place the window through
    /// <see cref="WindowPlacement.EnsureVisible"/> against the current monitors so it always lands
    /// fully on-screen — even after a monitor was unplugged or rearranged.
    /// </summary>
    private void RestorePlacement()
    {
        if (_boundsRestored || SuppressBoundsRestore) return;
        _boundsRestored = true;

        var saved = _config.Window;
        if (!_config.RememberWindow || saved is null || saved.Width <= 0 || saved.Height <= 0) return;

        var workAreas = System.Windows.Forms.Screen.AllScreens
            .Select(s => new Rect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height))
            .ToList();

        var placed = WindowPlacement.EnsureVisible(
            saved, workAreas,
            new System.Windows.Size(MinWidth, MinHeight), new System.Windows.Size(780, 520));

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placed.X; Top = placed.Y; Width = placed.Width; Height = placed.Height;
    }

    private static string? ScreenDeviceNameAt(double x, double y)
    {
        try { return System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)x, (int)y)).DeviceName; }
        catch { return null; }
    }

    // --- Settings gear ------------------------------------------------------

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        (Application.Current as App)?.OpenSettings();

    // Closing hides to tray instead of exiting; the app exits explicitly via the tray menu.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        HideLauncher();
    }
}
