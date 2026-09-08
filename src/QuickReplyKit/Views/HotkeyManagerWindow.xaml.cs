using System.IO;
using System.Windows;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Interop;
using QuickReplyKit.Services;

namespace QuickReplyKit.Views;

/// <summary>
/// The keyboard-shortcuts manager: lists the launcher hotkey plus every per-response instant-paste
/// bind (gesture, target, rich/plain), flags conflicts (a duplicate within the app, or one Windows
/// rejected as in-use), and offers Edit/Clear per bind. All conflict logic is delegated to
/// <see cref="HotkeyConflicts"/>.
/// </summary>
public partial class HotkeyManagerWindow : Window
{
    private readonly AppConfig _config;
    private readonly HotkeyService _hotkeys;
    private readonly LibraryService _library;
    private readonly App _app;

    private List<HotkeyRow> _allRows = new();

    public HotkeyManagerWindow(AppConfig config, HotkeyService hotkeys, LibraryService library, App app)
    {
        _config = config;
        _hotkeys = hotkeys;
        _library = library;
        _app = app;
        InitializeComponent();
        Rebuild();
    }

    /// <summary>One row of the list; public for XAML data-binding.</summary>
    public sealed record HotkeyRow(
        string Gesture, string Target, string Mode, string Status,
        bool HasProblem, bool IsResponse, string? RelativePath)
    {
        public bool HasMode => Mode.Length > 0;
    }

    // Rebuild the whole list from the live registration entries, then re-apply the current filter.
    private void Rebuild()
    {
        var dupes = HotkeyConflicts.Duplicates(_config.HotkeyGesture, _config.ResponseHotkeys);
        var rows = new List<HotkeyRow>();

        foreach (var e in _hotkeys.Entries)
        {
            bool dup = HotkeyConflicts.KeyOf(e.Gesture) is { } key && dupes.Contains(key);
            string status = dup ? "Duplicate of another QuickReplyKit shortcut"
                          : !e.Registered ? "In use by another app"
                          : "Active";
            bool problem = dup || !e.Registered;

            if (e.IsLauncher)
            {
                rows.Add(new HotkeyRow(e.Gesture, "Open QuickReplyKit (launcher)", "", status, problem, false, null));
            }
            else
            {
                var item = _library.FindByRelativePath(e.RelativePath!);
                string target = item?.Name ?? Path.GetFileNameWithoutExtension(e.RelativePath) + "  (not found)";
                rows.Add(new HotkeyRow(e.Gesture, target, e.Plain ? "Plain text" : "Rich text",
                    status, problem, true, e.RelativePath));
            }
        }

        _allRows = rows;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string q = FilterBox.Text.Trim();
        RowsList.ItemsSource = q.Length == 0
            ? _allRows
            : _allRows.Where(r =>
                r.Gesture.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Target.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

    // --- Row actions --------------------------------------------------------

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string rp) return;
        if (_library.FindByRelativePath(rp) is { } item) _app.OpenAssignHotkey(item);
        Rebuild();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string rp) return;
        _app.ClearResponseHotkey(rp);
        Rebuild();
    }
}
