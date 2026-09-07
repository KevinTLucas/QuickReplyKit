using System.IO;
using QuickReplyKit.Core.Diagnostics;
using Timer = System.Timers.Timer;

namespace QuickReplyKit.Core.Library;

/// <summary>
/// Watches the library root (recursively) for .json and folder changes and raises a single
/// debounced <see cref="Changed"/> event per burst, so the UI can rescan without a manual
/// rebuild. Recovers if the root is deleted/recreated. <see cref="Changed"/> fires on a
/// thread-pool thread — the consumer marshals to the UI thread.
/// </summary>
public sealed class LibraryWatcher : IDisposable
{
    private const string LegacyFolderSidecar = ".qrkfolder.json";
    private readonly string _root;
    private readonly Timer _debounce;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private volatile bool _attached;
    private bool _disposed;

    public event Action? Changed;

    public bool IsAttached => _attached;

    public LibraryWatcher(string libraryRoot, int debounceMs = 250)
    {
        _root = libraryRoot;
        _debounce = new Timer(debounceMs) { AutoReset = false };
        // Fire under the lock so a callback that slipped through cannot deliver Changed after
        // Dispose: either it runs first (Dispose then waits on the lock), or Dispose wins, sets
        // _disposed, and this returns without invoking.
        _debounce.Elapsed += (_, _) =>
        {
            lock (_gate)
            {
                if (_disposed) return;
                Diag.Verbose("LibraryWatcher", "Watcher", "activity delivered=true");
                Changed?.Invoke();
            }
        };
        Start();
    }

    private void Start()
    {
        Diag.Verbose("LibraryWatcher", "Watcher", "attach attempt=true");
        try
        {
            // A watcher needs an existing dir to attach to. Create the library folder only when its
            // parent already exists — never fabricate a deep tree for a temporarily-offline mount
            // (e.g. OneDrive), which would shadow the cloud library with a stray local folder. If the
            // root isn't attachable, skip watching gracefully; a later rescan/change picks it up.
            if (!Directory.Exists(_root))
            {
                var parent = Path.GetDirectoryName(_root);
                if (parent is null || !Directory.Exists(parent))
                {
                    _watcher = null;
                    _attached = false;
                    Diag.Warn("LibraryWatcher", "Watcher", "attach deferred rootAvailable=false");
                    return;
                }
                Directory.CreateDirectory(_root);
            }
            var w = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            w.Created += OnChange;
            w.Deleted += OnChange;
            w.Changed += OnChange;
            w.Renamed += OnChange;
            w.Error += OnError;
            w.EnableRaisingEvents = true;
            _watcher = w;
            _attached = true;
            Diag.Info("LibraryWatcher", "Watcher", "attach complete attached=true");
        }
        catch (Exception ex)
        {
            // Root unavailable right now; a later Error/retry re-attaches.
            _watcher = null;
            _attached = false;
            Diag.Warn("LibraryWatcher", "Watcher", "attach complete attached=false", ex);
        }
    }

    private void OnChange(object sender, FileSystemEventArgs e)
    {
        if (Path.GetFileName(e.Name ?? "").Equals(LegacyFolderSidecar, StringComparison.OrdinalIgnoreCase))
            return;

        // *.json files and folders (no extension) are relevant; ignore other file noise.
        string ext = Path.GetExtension(e.Name ?? "");
        if (ext.Length == 0 || ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            Diag.Verbose("LibraryWatcher", "Watcher", $"activity relevant=true changeType={e.ChangeType}");
            Trigger();
        }
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Buffer overflow or the watched dir was removed — rebuild the watcher, then rescan.
        Diag.Warn("LibraryWatcher", "Watcher", "recovery started=true", e.GetException());
        lock (_gate)
        {
            if (_disposed) return;
            try { _watcher?.Dispose(); } catch { /* ignore */ }
            _watcher = null;
            _attached = false;
            Start();
        }
        Diag.Info("LibraryWatcher", "Watcher", $"recovery complete attached={_watcher is not null}");
        Trigger();
    }

    private void Trigger()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _debounce.Stop();
            _debounce.Start();
        }
    }

    public void Dispose()
    {
        FileSystemWatcher? watcher;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _debounce.Dispose();   // inside the lock: no debounced Changed can fire after this
            watcher = _watcher;
            _watcher = null;
            _attached = false;
        }
        try { watcher?.Dispose(); } catch { /* ignore */ }
    }
}
