using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Search;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Core.Templates;
using QuickReplyKit.Core.Text;
using QuickReplyKit.Services;

namespace QuickReplyKit.ViewModels;

/// <summary>
/// Drives the launcher: nav tree, scoped live results, preview, empty states, and the three insert actions.
/// </summary>
public sealed class LauncherViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly IInsertService _insert;
    private readonly AppConfig _config;
    private readonly Func<DateTimeOffset> _now;
    private readonly DispatcherTimer _copiedHintTimer;
    private CancellationTokenSource? _previewCancellation;
    private ResponseItem? _previewResolvedItem;
    private TemplateResolutionResult? _previewResolution;
    private long _previewVersion;
    private int _actionRunning;

    public ObservableCollection<NavNodeVm> NavRoots { get; } = new();
    public ObservableCollection<ResponseRowVm> Results { get; } = new();

    /// <summary>Raised after an insert so the window can hide itself.</summary>
    public event Action? RequestHide;

    /// <summary>Raised when an action is stopped because a response script could not run.</summary>
    public event Action<string>? RequestScriptError;

    public RelayCommand PasteCommand { get; }
    public RelayCommand PastePlainCommand { get; }
    public RelayCommand CopyCommand { get; }

    public LauncherViewModel(LibraryService library, IInsertService insert, AppConfig config,
        Func<DateTimeOffset>? now = null)
    {
        _library = library;
        _insert = insert;
        _config = config;
        _now = now ?? (static () => DateTimeOffset.Now);
        _loading = library.IsLoading;   // deferred initial scan still running → show "Loading…"

        PasteCommand = new RelayCommand(_ => TriggerDefaultInsert(), _ => CanRunAction);
        PastePlainCommand = new RelayCommand(_ => TriggerPlainInsert(), _ => CanRunAction);
        CopyCommand = new RelayCommand(_ => _ = StartAction(CopySelectedAsync), _ => CanRunAction);

        // Copy keeps the launcher open, so it needs its own confirmation; this auto-clears it.
        _copiedHintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _copiedHintTimer.Tick += (_, _) => { _copiedHintTimer.Stop(); CopiedHint = ""; };

        BuildNav();
        SelectNav(NavRoots.First(n => n.Scope == NavScope.All));   // default landing view
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
            foreach (var node in Flatten(NavRoots))
                node.IsSelected = node == value;
            OnPropertyChanged(nameof(CurrentCategory));
            OnPropertyChanged(nameof(HasSearchScope));
            OnPropertyChanged(nameof(SearchScopeLabel));
            RefreshResults();
        }
    }

    public bool HasSearchScope => _selectedNav?.Scope == NavScope.Category;

    public string SearchScopeLabel => HasSearchScope ? _selectedNav!.Title : "";

    /// <summary>Return to global search without changing the current query.</summary>
    public void ClearSearchScope()
    {
        SelectNav(NavRoots.First(n => n.Scope == NavScope.All));
        RefreshResults();
    }

    // --- Results / selection ------------------------------------------------

    private ResponseRowVm? _selectedResult;
    public ResponseRowVm? SelectedResult
    {
        get => _selectedResult;
        set { if (Set(ref _selectedResult, value)) UpdatePreview(); }
    }

    public bool HasValidSelection => _selectedResult?.IsValid == true;
    private bool CanRunAction => HasValidSelection && Volatile.Read(ref _actionRunning) == 0;

    /// <summary>The response backing the current selection (valid or not), or null.</summary>
    public ResponseItem? SelectedItem => _selectedResult?.Item;

    /// <summary>Absolute library root, for the editor and file-management ops.</summary>
    public string LibraryRoot => _config.LibraryPath;

    /// <summary>Category folder to pre-select in the editor: the current nav category, else "".</summary>
    public string CurrentCategory =>
        _selectedNav?.Scope == NavScope.Category ? _selectedNav.CategoryPath : "";

    /// <summary>Every existing category folder path (sorted), for the editor's category picker.</summary>
    public IReadOnlyList<string> CategoryPaths()
    {
        var paths = new List<string>();
        void Walk(CategoryNode n)
        {
            if (!string.IsNullOrEmpty(n.RelativePath)) paths.Add(n.RelativePath);
            foreach (var c in n.Children) Walk(c);
        }
        Walk(_library.Snapshot.Root);
        paths.Sort(StringComparer.OrdinalIgnoreCase);
        return paths;
    }

    /// <summary>Enabled, valid, locally trusted scripts available to the editor variable menu.</summary>
    public IReadOnlyList<string> ScriptNames() => _library.Scripts
        .Where(x => x.IsValid && x.IsEnabled && x.IsTrusted)
        .Select(x => x.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public string CategoryFullPath(string categoryPath) =>
        string.IsNullOrEmpty(categoryPath) ? LibraryRoot
            : System.IO.Path.Combine(LibraryRoot, categoryPath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>Valid and invalid response files below one category, for destructive confirmations.</summary>
    public int ResponseCountUnder(string categoryPath) => _library.Snapshot.AllResponses.Count(r =>
        r.CategoryPath == categoryPath ||
        r.CategoryPath.StartsWith(categoryPath + "/", StringComparison.OrdinalIgnoreCase));

    /// <summary>Synchronously refresh after an explicit library mutation.</summary>
    public void ReloadLibrary()
    {
        _library.Reload();
        OnLibraryUpdated();
    }

    /// <summary>Select the row for a given library-relative path after a refresh, if it exists.</summary>
    public void SelectByRelativePath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return;
        var match = Results.FirstOrDefault(r => r.Item.RelativePath == relativePath);
        if (match != null) SelectedResult = match;
    }

    /// <summary>True while the deferred initial library scan is still running (drives the "Loading…" empty state).</summary>
    private bool _loading;

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

    private string _copiedHint = "";
    /// <summary>Transient "Copied ✓" confirmation shown after a Copy, auto-cleared after ~1.5s.</summary>
    public string CopiedHint { get => _copiedHint; private set { if (Set(ref _copiedHint, value)) OnPropertyChanged(nameof(HasCopiedHint)); } }

    public bool HasCopiedHint => !string.IsNullOrEmpty(_copiedHint);

    private bool _selectionHasVariables;
    /// <summary>True when the selected response contains <c>{{…}}</c> tokens resolved on insert.</summary>
    public bool SelectionHasVariables { get => _selectionHasVariables; private set => Set(ref _selectionHasVariables, value); }

    // --- Keyboard entry points (called from the window) ---------------------

    /// <summary>Move the results selection by <paramref name="delta"/> without moving focus.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        int idx = _selectedResult == null ? -1 : Results.IndexOf(_selectedResult);
        idx = Math.Clamp(idx + delta, 0, Results.Count - 1);
        SelectedResult = Results[idx];
    }

    public void TriggerDefaultInsert() => _ = TriggerDefaultInsertAsync();
    public void TriggerPlainInsert() => _ = TriggerPlainInsertAsync();
    internal Task TriggerDefaultInsertAsync() => StartAction(DefaultInsertAsync);
    internal Task TriggerPlainInsertAsync() => StartAction(PlainInsertAsync);
    internal Task PendingAction { get; private set; } = Task.CompletedTask;
    internal Task PreviewCompletion { get; private set; } = Task.CompletedTask;

    // --- Live update from the watcher (already marshalled to the Dispatcher) --

    public void OnLibraryUpdated()
    {
        _loading = false;   // the first scan (or any later refresh) has landed
        var selResultPath = _selectedResult?.Item.RelativePath;
        var scope = _selectedNav?.Scope;
        var path = _selectedNav?.CategoryPath;
        BuildNav();
        SelectNav(FindNav(scope, path) ?? NavRoots.First(n => n.Scope == NavScope.All));
        RefreshResults();

        // Preserve the result selection across a live refresh where the item still exists;
        // RefreshResults has otherwise reset it to the first row.
        if (!string.IsNullOrEmpty(selResultPath))
        {
            var match = Results.FirstOrDefault(r => r.Item.RelativePath == selResultPath);
            if (match != null) SelectedResult = match;
        }
    }

    // --- Internals ----------------------------------------------------------

    private void RefreshResults()
    {
        Results.Clear();
        int count;
        if (string.IsNullOrWhiteSpace(Query))
        {
            var items = ScopeItems(_selectedNav);
            foreach (var it in items) Results.Add(new ResponseRowVm(it, OnToggleFavorite));
            count = items.Count;
        }
        else
        {
            // Filter first so exact matches outside the active view cannot suppress fuzzy matches inside it.
            var hits = ResponseSearch.Rank(ScopeItems(_selectedNav), Query);
            foreach (var h in hits) Results.Add(new ResponseRowVm(h.Item, OnToggleFavorite, h.NameSpans, h.Snippet));
            count = hits.Count;
        }

        SelectedResult = Results.FirstOrDefault();
        UpdateEmptyState(count);
    }

    private IReadOnlyList<ResponseItem> ScopeItems(NavNodeVm? nav)
    {
        var snap = _library.Snapshot;
        return (nav?.Scope) switch
        {
            NavScope.Favorites => _library.Favorites(),
            NavScope.Recent => _library.Recent(),
            // All and category views include invalid files (muted, ⚠, not paste-able) so broken
            // responses can be found and fixed anywhere in the library.
            NavScope.All => ByValidThenName(snap.AllResponses),
            NavScope.Category => CategoryItems(snap, nav!.CategoryPath),
            _ => ByValidThenName(snap.AllResponses),
        };
    }

    private static IReadOnlyList<ResponseItem> CategoryItems(LibrarySnapshot snap, string path)
    {
        IEnumerable<ResponseItem> q = snap.AllResponses;   // include invalid so the ⚠ marker shows
        if (!string.IsNullOrEmpty(path))
            q = q.Where(r => r.CategoryPath == path ||
                             r.CategoryPath.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase));
        return ByValidThenName(q);
    }

    /// <summary>Valid rows first, then invalid, each group ordered by name.</summary>
    private static IReadOnlyList<ResponseItem> ByValidThenName(IEnumerable<ResponseItem> items) =>
        items.OrderBy(r => r.IsValid ? 0 : 1)
             .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
             .ToList();

    private void OnToggleFavorite(ResponseRowVm row)
    {
        _library.ToggleFavorite(row.Item);
        // In the Favorites view an unfavorited item must drop out.
        if (string.IsNullOrWhiteSpace(Query) && _selectedNav?.Scope == NavScope.Favorites)
            RefreshResults();
    }

    private void UpdatePreview()
    {
        CancelPreview();
        PreviewCompletion = Task.CompletedTask;
        var item = _selectedResult?.Item;
        if (item == null) { PreviewDocument = new FlowDocument(); PreviewMeta = ""; SelectionHasVariables = false; }
        else if (!item.IsValid) { PreviewDocument = ErrorDocument(item); PreviewMeta = "This file could not be read."; SelectionHasVariables = false; }
        else
        {
            PreviewMeta = BuildMeta(item);
            SelectionHasVariables = TemplateEngine.ContainsTokens(item.Html);
            if (_library.HasTrustedNativeScripts)
            {
                PreviewDocument = new FlowDocument();
                var cancellation = new CancellationTokenSource();
                _previewCancellation = cancellation;
                long version = _previewVersion;
                PreviewCompletion = UpdatePreviewAsync(item, version, cancellation);
            }
            else
            {
                // Preserve the immediate qrk-expr-1 preview path.
                var resolution = _library.ResolveTemplateDetailed(item.Html, _now());
                CachePreview(item, resolution);
                PreviewDocument = HtmlToFlowDocument.Convert(resolution.Html);
                PreviewCompletion = Task.CompletedTask;
            }
        }

        OnPropertyChanged(nameof(HasValidSelection));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task UpdatePreviewAsync(ResponseItem item, long version,
        CancellationTokenSource cancellation)
    {
        try
        {
            var resolution = await _library.ResolveTemplateDetailedAsync(item.Html, _now(), cancellation.Token);
            if (cancellation.IsCancellationRequested || version != _previewVersion ||
                !ReferenceEquals(_selectedResult?.Item, item)) return;
            CachePreview(item, resolution);
            PreviewDocument = HtmlToFlowDocument.Convert(resolution.Html);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (version == _previewVersion)
                PreviewDocument = HtmlToFlowDocument.Convert("[script error: evaluation-failed]");
            Diag.Warn("LauncherViewModel", "Preview", "template preview failed safely", ex);
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
                _previewCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelPreview()
    {
        _previewVersion++;
        var cancellation = _previewCancellation;
        _previewCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        _previewResolvedItem = null;
        _previewResolution = null;
    }

    private void CachePreview(ResponseItem item, TemplateResolutionResult resolution)
    {
        _previewResolvedItem = item;
        _previewResolution = resolution;
    }

    public void RefreshPreview() => UpdatePreview();

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
        var error = new Paragraph(new Run(item.Error ?? "This file could not be read."));
        if (System.Windows.Application.Current?.TryFindResource("Error") is System.Windows.Media.Brush brush)
            error.Foreground = brush;
        doc.Blocks.Add(error);
        return doc;
    }

    private void UpdateEmptyState(int count)
    {
        IsEmptyState = count == 0;
        if (count > 0) { EmptyStateText = ""; return; }

        if (_loading)
            EmptyStateText = "Loading library…";
        else if (_library.Snapshot.AllResponses.Count == 0)
            EmptyStateText = "No responses yet.\nAdd a .json to your library, or open the library folder to get started.";
        else if (!string.IsNullOrWhiteSpace(Query))
            EmptyStateText = _selectedNav?.Scope switch
            {
                NavScope.Category => $"No responses match “{Query}” in “{CurrentCategory}”.",
                NavScope.Favorites => $"No favorite responses match “{Query}”.",
                NavScope.Recent => $"No recent responses match “{Query}”.",
                _ => $"No responses match “{Query}”.",
            };
        else
            EmptyStateText = _selectedNav?.Scope switch
            {
                NavScope.Favorites => "No favorites yet.\nTap the star on a response to add one.",
                NavScope.Recent => "Nothing used yet.\nResponses you insert will show up here.",
                NavScope.Category => "No responses in this folder.",
                _ => "No responses.",
            };
    }

    private async Task DefaultInsertAsync()
    {
        var item = ValidSelectedItem();
        if (item == null) return;
        var resolved = await ResolvedCopyAsync(item);
        if (resolved is null) return;
        bool plain = _config.DefaultPasteMode == PasteMode.PlainText;
        Diag.Verbose("LauncherViewModel", "Action", $"insert requested mode={(plain ? "plain" : "rich")}");
        if (plain) _insert.InsertPlain(resolved);
        else _insert.InsertRich(resolved);
        _library.MarkUsed(item);
        RequestHide?.Invoke();
    }

    private async Task PlainInsertAsync()
    {
        var item = ValidSelectedItem();
        if (item == null) return;
        Diag.Verbose("LauncherViewModel", "Action", "insert requested mode=plain source=shortcut");
        var resolved = await ResolvedCopyAsync(item);
        if (resolved is null) return;
        _insert.InsertPlain(resolved);
        _library.MarkUsed(item);
        RequestHide?.Invoke();
    }

    /// <summary>
    /// Resolve an item's stored HTML for the "Edit &amp; insert" editor: greeting/date filled and
    /// includes expanded, so the user edits the actual message rather than the template.
    /// </summary>
    public string ResolveComposed(ResponseItem item) => Resolve(item.Html);
    public async Task<string> ResolveComposedAsync(ResponseItem item) =>
        (await ResolveDetailedAsync(item.Html, item)).Html;

    public async Task<string?> TryResolveComposedAsync(ResponseItem item)
    {
        var resolution = await ResolveDetailedAsync(item.Html, item);
        return Accept(resolution) ? resolution.Html : null;
    }

    /// <summary>
    /// Insert a one-off message composed in the editor (edited, NOT saved to any file). Resolves any
    /// remaining <c>{{…}}</c> tokens for safety (idempotent when token-free), then runs the real
    /// insert pipeline into the captured target window and hides the launcher. Touches no file.
    /// </summary>
    public void InsertComposed(string html, bool plain) =>
        _ = StartAction(() => InsertComposedAsync(html, plain));

    internal async Task InsertComposedAsync(string html, bool plain)
    {
        var resolution = await ResolveDetailedAsync(html);
        if (!Accept(resolution)) return;
        var item = new ResponseItem { Html = resolution.Html };
        if (plain) _insert.InsertPlain(item);
        else _insert.InsertRich(item);
        RequestHide?.Invoke();
    }

    private async Task CopySelectedAsync()
    {
        var item = ValidSelectedItem();
        if (item == null) return;
        var resolved = await ResolvedCopyAsync(item);
        if (resolved is null) return;
        _insert.Copy(resolved);
        _library.MarkUsed(item);
        // Copy leaves the launcher open so the user can keep browsing; flash a brief confirmation.
        CopiedHint = "Copied ✓";
        _copiedHintTimer.Stop();
        _copiedHintTimer.Start();
    }

    private ResponseItem? ValidSelectedItem() =>
        _selectedResult?.IsValid == true ? _selectedResult.Item : null;

    /// <summary>Resolve <c>{{…}}</c> tokens in the stored HTML against the current library and clock.</summary>
    private string Resolve(string html) => _library.ResolveTemplate(html, _now());

    private async Task<TemplateResolutionResult> ResolveDetailedAsync(string html, ResponseItem? item = null)
    {
        if (item is not null && ReferenceEquals(item, _selectedResult?.Item) &&
            !PreviewCompletion.IsCompleted)
            await PreviewCompletion;
        if (item is not null && ReferenceEquals(item, _previewResolvedItem) &&
            _previewResolution is not null)
            return _previewResolution;

        DateTimeOffset now = _now();
        return _library.HasTrustedNativeScripts
            ? await _library.ResolveTemplateDetailedAsync(html, now)
            : _library.ResolveTemplateDetailed(html, now);
    }

    private async Task<ResponseItem?> ResolvedCopyAsync(ResponseItem item)
    {
        var resolution = await ResolveDetailedAsync(item.Html, item);
        if (!Accept(resolution)) return null;
        string html = resolution.Html;
        return html == item.Html ? item : CopyWithHtml(item, html);
    }

    private bool Accept(TemplateResolutionResult resolution)
    {
        if (resolution.Success) return true;
        Diag.Warn("LauncherViewModel", "Script", "response action blocked by script failure");
        RequestScriptError?.Invoke(resolution.ErrorText);
        return false;
    }

    private static ResponseItem CopyWithHtml(ResponseItem item, string html) => new()
    {
        Name = item.Name,
        RelativePath = item.RelativePath,
        FullPath = item.FullPath,
        CategoryPath = item.CategoryPath,
        App = item.App,
        Tags = item.Tags,
        Html = html,
        Favorite = item.Favorite,
        Created = item.Created,
        Modified = item.Modified,
        IsValid = item.IsValid,
        Error = item.Error,
    };

    private Task StartAction(Func<Task> action)
    {
        if (Interlocked.CompareExchange(ref _actionRunning, 1, 0) != 0)
            return Task.CompletedTask;
        CommandManager.InvalidateRequerySuggested();
        PendingAction = RunAction(action);
        return PendingAction;
    }

    private async Task RunAction(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { Diag.Warn("LauncherViewModel", "Action", "action failed safely", ex); }
        finally
        {
            Interlocked.Exchange(ref _actionRunning, 0);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void BuildNav()
    {
        NavRoots.Clear();
        NavRoots.Add(new NavNodeVm("Favorites", "⭐", NavScope.Favorites));
        NavRoots.Add(new NavNodeVm("Recent", "🕘", NavScope.Recent));
        var all = new NavNodeVm("All Responses", "📋", NavScope.All);
        NavRoots.Add(all);

        foreach (var child in _library.Snapshot.Root.Children)
            NavRoots.Add(BuildCategory(child));
    }

    /// <summary>Selects one nav node, clearing any stale selection flags so exactly one is marked.</summary>
    private void SelectNav(NavNodeVm node)
    {
        foreach (var n in Flatten(NavRoots))
            if (n.IsSelected && n != node) n.IsSelected = false;
        _selectedNav = node;
        node.IsSelected = true;
        OnPropertyChanged(nameof(SelectedNav));
        OnPropertyChanged(nameof(CurrentCategory));
        OnPropertyChanged(nameof(HasSearchScope));
        OnPropertyChanged(nameof(SearchScopeLabel));
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
