using QuickReplyKit.Core.Library;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Search;
using QuickReplyKit.Core.Scripts;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Core.Templates;

namespace QuickReplyKit.Core;

/// <summary>
/// Headless coordinator over the library: seeds on first run, scans into a
/// <see cref="LibrarySnapshot"/>, watches for on-disk changes, and exposes search, favorites,
/// recent, and favorite/used mutations. <see cref="Updated"/> fires on a thread-pool thread
/// (the <see cref="LibraryWatcher"/>'s thread) after the snapshot is rebuilt — WPF consumers
/// must marshal to the Dispatcher. Guarded so a late watcher callback after <see cref="Dispose"/>
/// is a no-op.
/// </summary>
public sealed class LibraryService : IDisposable
{
    private string _root;
    private readonly string? _recentPath;   // null → the global %APPDATA% recent file
    private readonly RecentStore _recent;
    private readonly ScriptTrustLedger _scriptTrust;
    private ScriptCatalog _scripts;
    private LibraryWatcher? _watcher;
    private readonly object _gate = new();
    private bool _disposed;

    public LibrarySnapshot Snapshot { get; private set; }

    /// <summary>
    /// True until the first scan completes when the initial scan was deferred (see
    /// <see cref="LoadInitial"/>). Lets the UI show a "Loading…" state instead of "empty".
    /// </summary>
    public bool IsLoading { get; private set; }

    /// <summary>Whether the library folder is being watched for live changes (diagnostics).</summary>
    public bool IsWatching => _watcher?.IsAttached == true;

    /// <summary>Portable script definitions and their effective local status.</summary>
    public IReadOnlyList<ScriptCatalogItem> Scripts => _scripts.Items;

    /// <summary>Raised after the library changed on disk and <see cref="Snapshot"/> was rebuilt.</summary>
    public event Action? Updated;

    public LibraryService(string libraryRoot, string? seedSourceDir = null,
        string? recentPath = null, bool watch = true, bool deferInitialScan = false,
        ScriptTrustLedger? scriptTrust = null)
    {
        _root = libraryRoot;
        _recentPath = recentPath;

        if (!string.IsNullOrEmpty(seedSourceDir))
            LibrarySeeder.SeedIfEmpty(_root, seedSourceDir);

        _recent = recentPath is null ? RecentStore.Load() : RecentStore.Load(recentPath);
        _scriptTrust = scriptTrust ?? ScriptTrustLedger.Open().Value!;

        // Deferred: start with an empty snapshot so the window can show immediately; the caller runs
        // LoadInitial() on a background thread. Otherwise scan up front (tests, self-tests, headless).
        if (deferInitialScan)
        {
            Snapshot = LibraryScanner.Empty(_root);
            _scripts = ScriptCatalog.Empty(_scriptTrust);
            IsLoading = true;
        }
        else
        {
            Snapshot = LibraryScanner.Scan(_root);
            _scripts = ScriptCatalog.Load(_root, _scriptTrust);
        }

        if (watch)
        {
            _watcher = new LibraryWatcher(_root);
            _watcher.Changed += OnLibraryChanged;
        }
    }

    /// <summary>
    /// Runs the deferred initial scan (call on a background thread), then raises <see cref="Updated"/>
    /// so the UI populates via its normal Dispatcher path. No-op after <see cref="Dispose"/>.
    /// </summary>
    public void LoadInitial()
    {
        lock (_gate) { if (_disposed) return; }
        Reload();
        IsLoading = false;
        Action? handler;
        lock (_gate) { if (_disposed) return; handler = Updated; }
        handler?.Invoke();
    }

    private void OnLibraryChanged()
    {
        lock (_gate) { if (_disposed) return; }
        Reload();
        Action? handler;
        lock (_gate) { if (_disposed) return; handler = Updated; }
        handler?.Invoke();
    }

    /// <summary>Rescans the library root into a fresh <see cref="Snapshot"/>.</summary>
    public void Reload()
    {
        Snapshot = LibraryScanner.Scan(_root);
        _scripts = ScriptCatalog.Load(_root, _scriptTrust);
    }

    /// <summary>
    /// Point the library at a new root: rescan it and (if watching) re-attach the watcher there.
    /// No-op after <see cref="Dispose"/>. Callers refresh their view via <see cref="Snapshot"/>.
    /// </summary>
    public void Repoint(string newRoot)
    {
        lock (_gate) { if (_disposed) return; }
        if (string.Equals(_root, newRoot, StringComparison.OrdinalIgnoreCase)) { Reload(); return; }

        _root = newRoot;
        Reload();
        if (_watcher != null)
        {
            _watcher.Changed -= OnLibraryChanged;
            _watcher.Dispose();
            _watcher = new LibraryWatcher(_root);
            _watcher.Changed += OnLibraryChanged;
        }
    }

    /// <summary>Valid response at a given library-relative path (case-insensitive), or null.</summary>
    public ResponseItem? FindByRelativePath(string relativePath) =>
        Snapshot.ValidResponses.FirstOrDefault(
            r => string.Equals(r.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>First valid response whose name matches (case-insensitive), or null. Backs template includes.</summary>
    public ResponseItem? FindByName(string name) =>
        Snapshot.ValidResponses.FirstOrDefault(
            r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Resolve built-ins, response includes, and trusted scripts against one library snapshot.</summary>
    public string ResolveTemplate(string html, DateTimeOffset now)
        => ResolveTemplateDetailed(html, now).Html;

    public TemplateResolutionResult ResolveTemplateDetailed(string html, DateTimeOffset now)
    {
        var snapshot = Snapshot;
        var scripts = _scripts;
        return TemplateEngine.ResolveDetailed(html,
            name => snapshot.ValidResponses.FirstOrDefault(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))?.Html,
            now, scripts.Evaluate);
    }

    /// <summary>
    /// Global, ranked search over the whole library (delegates to <see cref="ResponseSearch"/>).
    /// <paramref name="scope"/> is the currently selected category path (or ""); in-scope hits rank first.
    /// </summary>
    public IReadOnlyList<SearchHit> Search(string query, string scope = "") =>
        ResponseSearch.Rank(Snapshot.AllResponses, query, scope);

    /// <summary>All favorited valid responses, by name.</summary>
    public IReadOnlyList<ResponseItem> Favorites() =>
        Snapshot.Favorites.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Recently used responses, most-recent-first, mapped back onto the current snapshot.</summary>
    public IReadOnlyList<ResponseItem> Recent()
    {
        var byPath = new Dictionary<string, ResponseItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Snapshot.ValidResponses)
            byPath[r.RelativePath] = r;

        var list = new List<ResponseItem>();
        foreach (var (rel, _) in _recent.Items)
            if (byPath.TryGetValue(rel, out var item)) list.Add(item);
        return list;
    }

    /// <summary>Flips the favorite flag and rewrites the response file so it travels when shared.</summary>
    public void ToggleFavorite(ResponseItem item)
    {
        if (!item.IsValid) return;
        item.Favorite = !item.Favorite;
        ResponseFile.Save(item, _root);
    }

    /// <summary>Records a use in the local recent list.</summary>
    public void MarkUsed(ResponseItem item)
    {
        if (string.IsNullOrEmpty(item.RelativePath)) return;
        _recent.Add(item.RelativePath);
        if (_recentPath is null) _recent.Save(); else _recent.Save(_recentPath);
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        if (_watcher != null)
        {
            _watcher.Changed -= OnLibraryChanged;
            _watcher.Dispose();
        }
    }
}
