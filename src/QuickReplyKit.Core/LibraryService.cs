using QuickReplyKit.Core.Library;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Search;
using QuickReplyKit.Core.Storage;

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
    private readonly string _root;
    private readonly string? _recentPath;   // null → the global %APPDATA% recent file
    private readonly RecentStore _recent;
    private readonly LibraryWatcher? _watcher;
    private readonly object _gate = new();
    private bool _disposed;

    public LibrarySnapshot Snapshot { get; private set; }

    /// <summary>Raised after the library changed on disk and <see cref="Snapshot"/> was rebuilt.</summary>
    public event Action? Updated;

    public LibraryService(string libraryRoot, string? seedSourceDir = null,
        string? recentPath = null, bool watch = true)
    {
        _root = libraryRoot;
        _recentPath = recentPath;

        if (!string.IsNullOrEmpty(seedSourceDir))
            LibrarySeeder.SeedIfEmpty(_root, seedSourceDir);

        _recent = recentPath is null ? RecentStore.Load() : RecentStore.Load(recentPath);
        Snapshot = LibraryScanner.Scan(_root);

        if (watch)
        {
            _watcher = new LibraryWatcher(_root);
            _watcher.Changed += OnLibraryChanged;
        }
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
    public void Reload() => Snapshot = LibraryScanner.Scan(_root);

    /// <summary>Global, ranked search over the whole library (delegates to <see cref="ResponseSearch"/>).</summary>
    public IReadOnlyList<ResponseItem> Search(string query) =>
        ResponseSearch.Search(Snapshot.AllResponses, query);

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
