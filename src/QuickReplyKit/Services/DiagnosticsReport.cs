using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Services;

internal static class DiagnosticsReport
{
    public static string Build(AppConfig config, LibraryService library,
        IReadOnlyList<HotkeyService.Entry> hotkeys, string version)
    {
        var snapshot = library.Snapshot;
        int registered = hotkeys.Count(x => !x.IsLauncher && x.Registered);
        int configured = config.ResponseHotkeys.Count;
        int valid = snapshot.AllResponses.Count(x => x.IsValid);

        var text = new StringBuilder();
        void Row(string name, string value) => text.Append(name.PadRight(22)).Append(": ").AppendLine(value);

        Row("Version", version);
        Row("Runtime", RuntimeInformation.FrameworkDescription);
        Row("OS", RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture + ")");
        text.AppendLine();
        Row("Launcher hotkey", $"{config.HotkeyGesture} ({(hotkeys.Any(x => x.IsLauncher && x.Registered) ? "registered" : "not registered")})");
        Row("Response hotkeys", $"{configured} configured, {registered} registered, {configured - registered} failed");
        Row("Default paste", config.DefaultPasteMode.ToString());
        Row("Theme", config.Theme.ToString());
        text.AppendLine();
        Row("Library state", library.IsLoading ? "loading" : "ready");
        Row("Responses", $"{snapshot.AllResponses.Count} ({valid} valid, {snapshot.AllResponses.Count - valid} invalid)");
        Row("Watcher", library.IsWatching ? "attached" : "not attached");
        Row("Logging", Diag.IsEnabled
            ? Diag.Threshold == LogLevel.Verbose ? "active (verbose)" : "active (normal)"
            : "unavailable");
        text.AppendLine();
        Row("Library folder", Redact(config.LibraryPath));
        Row("Config file", Redact(AppPaths.ConfigPath));
        Row("Logs folder", Redact(AppPaths.LogsDir));
        Row("Backup folder", Redact(config.BackupPath));
        Row("Trash folder", Redact(AppPaths.TrashDir));
        return text.ToString();
    }

    internal static string Redact(string path)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar);
        return path.StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? "%USERPROFILE%" + path[profile.Length..]
            : path;
    }
}
