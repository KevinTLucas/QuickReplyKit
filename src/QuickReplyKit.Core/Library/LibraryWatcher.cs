using System.IO;
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
    private readonly string _root;
    private readonly Timer _debounce;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public event Action? Changed;

    public LibraryWatcher(string libraryRoot, int debounceMs = 250)
    {
        _root = libraryRoot;
        _debounce = new Timer(debounceMs) { AutoReset = false };
        _debounce.Elapsed += (_, _) => Changed?.Invoke();
        Start();
    }

    private void Start()
    {
        try
        {
            Directory.CreateDirectory(_root); // a watcher needs an existing dir to attach to
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
        }
        catch
        {
            // Root unavailable right now; a later Error/retry re-attaches.
            _watcher = null;
        }
    }

    private void OnChange(object sender, FileSystemEventArgs e)
    {
        // *.json files and folders (no extension) are relevant; ignore other file noise.
        string ext = Path.GetExtension(e.Name ?? "");
        if (ext.Length == 0 || ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            Trigger();
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Buffer overflow or the watched dir was removed — rebuild the watcher, then rescan.
        lock (_gate)
        {
            if (_disposed) return;
            try { _watcher?.Dispose(); } catch { /* ignore */ }
            _watcher = null;
            Start();
        }
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
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try { _watcher?.Dispose(); } catch { /* ignore */ }
        _debounce.Dispose();
    }
}
