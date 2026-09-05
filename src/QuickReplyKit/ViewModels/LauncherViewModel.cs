using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Core.Text;
using QuickReplyKit.Services;

namespace QuickReplyKit.ViewModels;

/// <summary>
/// Drives the launcher: nav tree, live results (scope when the query is empty, global search
/// otherwise), preview, empty states, and the three insert actions.
/// </summary>
public sealed class LauncherViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly IInsertService _insert;
    private readonly AppConfig _config;

    public ObservableCollection<NavNodeVm> NavRoots { get; } = new();
    public ObservableCollection<ResponseRowVm> Results { get; } = new();

    /// <summary>Raised after an insert so the window can hide itself.</summary>
    public event Action? RequestHide;

    public RelayCommand PasteCommand { get; }
    public RelayCommand PastePlainCommand { get; }
    public RelayCommand CopyCommand { get; }

    public LauncherViewModel(LibraryService library, IInsertService insert, AppConfig config)
    {
        _library = library;
        _insert = insert;
        _config = config;

        PasteCommand = new RelayCommand(_ => DefaultInsert(), _ => HasValidSelection);
        PastePlainCommand = new RelayCommand(_ => PlainInsert(), _ => HasValidSelection);
        CopyCommand = new RelayCommand(_ => CopySelected(), _ => HasValidSelection);

        BuildNav();
        RefreshResults();
    }

    // --- Search / scope -----------------------------------------------------

    private string _query = "";
    public string Query
    {
        get => _query;
        set { if (Set(ref _query, value)) RefreshResults(); }
    }

    private NavNodeVm? _selectedNav;
    public NavNodeVm? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (!Set(ref _selectedNav, value)) return;
            if (string.IsNullOrWhiteSpace(Query)) RefreshResults();  // scope only matters with no query
        }
    }

    // --- Results / selection ------------------------------------------------

    private ResponseRowVm? _selectedResult;
    public ResponseRowVm? SelectedResult
    {
        get => _selectedResult;
        set { if (Set(ref _selectedResult, value)) UpdatePreview(); }
    }

    public bool HasValidSelection => _selectedResult?.IsValid == true;

    private bool _isEmptyState;
    public bool IsEmptyState { get => _isEmptyState; private set => Set(ref _isEmptyState, value); }

    private string _emptyStateText = "";
    public string EmptyStateText { get => _emptyStateText; private set => Set(ref _emptyStateText, value); }

    // --- Preview ------------------------------------------------------------

    private FlowDocument _previewDocument = new();
    public FlowDocument PreviewDocument { get => _previewDocument; private set => Set(ref _previewDocument, value); }

    private string _previewMeta = "";
    public string PreviewMeta { get => _previewMeta; private set { if (Set(ref _previewMeta, value)) OnPropertyChanged(nameof(HasPreviewMeta)); } }

    public bool HasPreviewMeta => !string.IsNullOrEmpty(_previewMeta);

    // --- Keyboard entry points (called from the window) ---------------------

    /// <summary>Move the results selection by <paramref name="delta"/> without moving focus.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        int idx = _selectedResult == null ? -1 : Results.IndexOf(_selectedResult);
        idx = Math.Clamp(idx + delta, 0, Results.Count - 1);
        SelectedResult = Results[idx];
    }

    public void TriggerDefaultInsert() => DefaultInsert();
    public void TriggerPlainInsert() => PlainInsert();

    // --- Live update from the watcher (already marshalled to the Dispatcher) --

    public void OnLibraryUpdated()
    {
        var scope = _selectedNav?.Scope;
        var path = _selectedNav?.CategoryPath;
        BuildNav();
        var restored = FindNav(scope, path);
        if (restored != null) { _selectedNav = restored; restored.IsSelected = true; OnPropertyChanged(nameof(SelectedNav)); }
        RefreshResults();
    }

    // --- Internals ----------------------------------------------------------

    private void RefreshResults()
    {
        IReadOnlyList<ResponseItem> items = string.IsNullOrWhiteSpace(Query)
            ? ScopeItems(_selectedNav)
            : _library.Search(Query);

        Results.Clear();
        foreach (var it in items) Results.Add(new ResponseRowVm(it, OnToggleFavorite));

        SelectedResult = Results.FirstOrDefault();
        UpdateEmptyState(items.Count);
    }

    private IReadOnlyList<ResponseItem> ScopeItems(NavNodeVm? nav)
    {
        var snap = _library.Snapshot;
        return (nav?.Scope) switch
        {
            NavScope.Favorites => _library.Favorites(),
            NavScope.Recent => _library.Recent(),
            NavScope.All => snap.ValidResponses.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            NavScope.Category => CategoryItems(snap, nav!.CategoryPath),
            _ => snap.ValidResponses.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private static IReadOnlyList<ResponseItem> CategoryItems(LibrarySnapshot snap, string path)
    {
        IEnumerable<ResponseItem> q = snap.AllResponses;   // include invalid so the ⚠ marker shows
        if (!string.IsNullOrEmpty(path))
            q = q.Where(r => r.CategoryPath == path ||
                             r.CategoryPath.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase));
        return q.OrderBy(r => r.IsValid ? 0 : 1)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private void OnToggleFavorite(ResponseRowVm row)
    {
        _library.ToggleFavorite(row.Item);
        // In the Favorites view an unfavorited item must drop out.
        if (string.IsNullOrWhiteSpace(Query) && _selectedNav?.Scope == NavScope.Favorites)
            RefreshResults();
    }

    private void UpdatePreview()
    {
        var item = _selectedResult?.Item;
        if (item == null) { PreviewDocument = new FlowDocument(); PreviewMeta = ""; }
        else if (!item.IsValid) { PreviewDocument = ErrorDocument(item); PreviewMeta = "This file could not be read."; }
        else { PreviewDocument = HtmlToFlowDocument.Convert(item.Html); PreviewMeta = BuildMeta(item); }

        OnPropertyChanged(nameof(HasValidSelection));
        CommandManager.InvalidateRequerySuggested();
    }

    private static string BuildMeta(ResponseItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(item.App)) parts.Add("App: " + item.App);
        if (item.Tags.Count > 0) parts.Add("Tags: " + string.Join(", ", item.Tags));
        return string.Join("        ", parts);
    }

    private static FlowDocument ErrorDocument(ResponseItem item)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13,
        };
        doc.Blocks.Add(new Paragraph(new Bold(new Run(item.Name + ".json"))));
        doc.Blocks.Add(new Paragraph(new Run(item.Error ?? "This file could not be read."))
        {
            Foreground = System.Windows.Media.Brushes.Firebrick,
        });
        return doc;
    }

    private void UpdateEmptyState(int count)
    {
        IsEmptyState = count == 0;
        if (count > 0) { EmptyStateText = ""; return; }

        if (_library.Snapshot.AllResponses.Count == 0)
            EmptyStateText = "No responses yet.\nAdd a .json to your library, or open the library folder to get started.";
        else if (!string.IsNullOrWhiteSpace(Query))
            EmptyStateText = $"No responses match “{Query}”.";
        else
            EmptyStateText = _selectedNav?.Scope switch
            {
                NavScope.Favorites => "No favorites yet.\nTap the star on a response to add one.",
                NavScope.Recent => "Nothing used yet.\nResponses you insert will show up here.",
                NavScope.Category => "No responses in this folder.",
                _ => "No responses.",
            };
    }

    private void DefaultInsert()
    {
        var item = ValidSelectedItem();
        if (item == null) return;
        if (_config.DefaultPasteMode == PasteMode.PlainText) _insert.InsertPlain(item);
        else _insert.InsertRich(item);
        _library.MarkUsed(item);
        RequestHide?.Invoke();
    }

    private void PlainInsert()
    {
        var item = ValidSelectedItem();
        if (item == null) return;
        _insert.InsertPlain(item);
        _library.MarkUsed(item);
        RequestHide?.Invoke();
    }

    private void CopySelected()
    {
        var item = ValidSelectedItem();
        if (item == null) return;
        _insert.Copy(item);
        _library.MarkUsed(item);
        // Copy leaves the launcher open so the user can keep browsing.
    }

    private ResponseItem? ValidSelectedItem() =>
        _selectedResult?.IsValid == true ? _selectedResult.Item : null;

    private void BuildNav()
    {
        NavRoots.Clear();
        NavRoots.Add(new NavNodeVm("Favorites", "⭐", NavScope.Favorites));
        NavRoots.Add(new NavNodeVm("Recent", "🕘", NavScope.Recent));
        var all = new NavNodeVm("All Responses", "📋", NavScope.All);
        NavRoots.Add(all);

        foreach (var child in _library.Snapshot.Root.Children)
            NavRoots.Add(BuildCategory(child));

        _selectedNav = all;      // default landing view; setter side effects not needed here
        all.IsSelected = true;
        OnPropertyChanged(nameof(SelectedNav));
    }

    private static NavNodeVm BuildCategory(CategoryNode node)
    {
        var vm = new NavNodeVm(node.Name, "📁", NavScope.Category, node.RelativePath) { IsExpanded = true };
        foreach (var child in node.Children)
            vm.Children.Add(BuildCategory(child));
        return vm;
    }

    private NavNodeVm? FindNav(NavScope? scope, string? path)
    {
        if (scope == null) return null;
        foreach (var n in Flatten(NavRoots))
            if (n.Scope == scope && (scope != NavScope.Category || n.CategoryPath == path))
                return n;
        return null;
    }

    private static IEnumerable<NavNodeVm> Flatten(IEnumerable<NavNodeVm> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Flatten(n.Children)) yield return c;
        }
    }
}
