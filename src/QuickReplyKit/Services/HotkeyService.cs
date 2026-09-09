using QuickReplyKit.Core;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Interop;

namespace QuickReplyKit.Services;

/// <summary>
/// The single owner of every global hotkey: the launcher hotkey and all per-response instant-paste
/// binds. Rebuilds the whole registration set from <see cref="AppConfig"/> on demand (startup and
/// after any change), records the outcome of each registration for the manager to display, and runs
/// the clipboard-safe paste (templates resolved fresh each press) for a bound response without ever
/// showing a window. Registration must happen on the UI thread (it owns the message loop).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    /// <summary>One live binding and whether Windows accepted it (false → conflicted / in use).</summary>
    public readonly record struct Entry(string Gesture, bool IsLauncher, string? RelativePath, bool Plain, bool Registered);

    private readonly HotkeyRegistrar _registrar = new();
    private readonly AppConfig _config;
    private readonly LibraryService _library;
    private readonly IInsertService _insert;
    private readonly Func<DateTimeOffset> _now;

    private string _launcherGesture;
    private List<Entry> _entries = new();

    /// <summary>Raised on the UI thread when the launcher hotkey is pressed.</summary>
    public event Action? LauncherPressed;
    /// <summary>Raised when an instant paste is blocked by a restricted script failure.</summary>
    public event Action<string>? ScriptFailed;

    public HotkeyService(AppConfig config, LibraryService library, IInsertService insert, Func<DateTimeOffset>? now = null)
    {
        _config = config;
        _library = library;
        _insert = insert;
        _now = now ?? (static () => DateTimeOffset.Now);
        _launcherGesture = config.HotkeyGesture;
    }

    /// <summary>The current bindings and their registration status (launcher first, then binds).</summary>
    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>True if the launcher hotkey is currently registered with Windows.</summary>
    public bool LauncherRegistered => _entries.FirstOrDefault(e => e.IsLauncher).Registered;

    /// <summary>(Re)register the launcher hotkey plus every per-response bind from config.</summary>
    public void ReloadAll()
    {
        _registrar.UnregisterAll();
        var entries = new List<Entry>();

        entries.Add(RegisterOne(_launcherGesture, isLauncher: true, relativePath: null, plain: false));
        foreach (var b in _config.ResponseHotkeys)
            entries.Add(RegisterOne(b.Gesture, isLauncher: false, b.RelativePath, b.Plain));

        _entries = entries;
        int ok = entries.Count(e => e.Registered);
        Diag.Info("HotkeyService", "Hotkey",
            $"registration complete registered={ok} configured={entries.Count} responseBindings={_config.ResponseHotkeys.Count}");
    }

    private Entry RegisterOne(string gesture, bool isLauncher, string? relativePath, bool plain)
    {
        bool ok = false;
        if (HotkeyConflicts.KeyOf(gesture) is { } key)
        {
            Action action = isLauncher
                ? () => LauncherPressed?.Invoke()
                : () => InstantPaste(relativePath!, plain);
            ok = _registrar.Register(key, action);
        }
        if (!ok)
            Diag.Warn("HotkeyService", "Hotkey",
                $"registration failed target={(isLauncher ? "launcher" : "response")}");
        return new Entry(gesture, isLauncher, relativePath, plain, ok);
    }

    /// <summary>
    /// Try to move the launcher to <paramref name="gesture"/>: re-register everything with the new
    /// launcher combo and keep it only if Windows accepted it, otherwise revert. Returns success.
    /// The caller persists <see cref="AppConfig.HotkeyGesture"/> on true.
    /// </summary>
    public bool TryRebindLauncher(string gesture)
    {
        string previous = _launcherGesture;
        _launcherGesture = gesture;
        ReloadAll();
        if (LauncherRegistered) return true;
        _launcherGesture = previous;   // roll back to the working combo
        ReloadAll();
        return false;
    }

    internal void InstantPaste(string relativePath, bool plain)
    {
        // Re-resolve against the live snapshot: the library may have changed since registration.
        var item = _library.FindByRelativePath(relativePath);
        if (item is null)
        {
            Diag.Warn("HotkeyService", "Hotkey", "instant-paste target unavailable");
            return;
        }

        Diag.Verbose("HotkeyService", "Hotkey", $"instant-paste invoked mode={(plain ? "plain" : "rich")}");
        var resolution = _library.ResolveTemplateDetailed(item.Html, _now());
        if (!resolution.Success)
        {
            Diag.Warn("HotkeyService", "Script", "instant-paste blocked by script failure");
            ScriptFailed?.Invoke(resolution.ErrorText);
            return;
        }
        var resolved = new ResponseItem { Html = resolution.Html };
        if (plain) _insert.InsertPlain(resolved);
        else _insert.InsertRich(resolved);
        _library.MarkUsed(item);
    }

    public void Dispose() => _registrar.Dispose();
}
