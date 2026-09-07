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
        if (string.IsNullOrWhiteSpace(path)) return "(not configured)";

        string normalized;
        try { normalized = Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar); }
        catch { return "[invalid location]"; }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            .TrimEnd(Path.DirectorySeparatorChar);
        if (normalized.Equals(profile, StringComparison.OrdinalIgnoreCase)) return "%USERPROFILE%";
        if (normalized.StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return "%USERPROFILE%" + normalized[profile.Length..];

        // Also hide 8.3 aliases such as C:\Users\KEVIN_~1, which GetFullPath does not expand.
        string root = Path.GetPathRoot(normalized) ?? "";
        string usersPrefix = root + "Users" + Path.DirectorySeparatorChar;
        if (normalized.StartsWith(usersPrefix, StringComparison.OrdinalIgnoreCase))
        {
            int remainder = normalized.IndexOf(Path.DirectorySeparatorChar, usersPrefix.Length);
            return remainder < 0 ? "%USERPROFILE%" : "%USERPROFILE%" + normalized[remainder..];
        }

        return normalized.StartsWith("\\\\", StringComparison.Ordinal)
            ? "[network location]"
            : "[custom location]";
    }
}
