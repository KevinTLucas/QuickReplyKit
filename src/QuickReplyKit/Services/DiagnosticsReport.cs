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
        var scripts = library.Scripts;

        var text = new StringBuilder();
        void Row(string name, string value) => text.Append(name.PadRight(22)).Append(": ").AppendLine(value);

        Row("Version", version);
        Row("Runtime", RuntimeInformation.FrameworkDescription);
        Row("OS", RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture + ")");
        text.AppendLine();
        Row("Launcher hotkey", hotkeys.Any(x => x.IsLauncher && x.Registered) ? "registered" : "not registered");
        Row("Response hotkeys", $"{configured} configured, {registered} registered, {configured - registered} failed");
        Row("Default paste", config.DefaultPasteMode.ToString());
        Row("Theme", config.Theme.ToString());
        text.AppendLine();
        Row("Library state", library.IsLoading ? "loading" : "ready");
        Row("Responses", $"{snapshot.AllResponses.Count} ({valid} valid, {snapshot.AllResponses.Count - valid} invalid)");
        Row("Watcher", library.IsWatching ? "attached" : "not attached");
        Row("Scripts", $"{scripts.Count} total, {scripts.Count(x => x.IsEnabled)} enabled, " +
            $"{scripts.Count(x => x.IsTrusted)} trusted, {scripts.Count(x => !x.IsValid)} invalid");
        Row("Logging", Diag.IsEnabled
            ? Diag.Threshold == LogLevel.Verbose ? "active (verbose)" : "active (normal)"
            : "unavailable");
        text.AppendLine();
        Row("Library folder", ResolvePath(config.LibraryPath));
        Row("Config file", AppPaths.ConfigPath);
        Row("Logs folder", AppPaths.LogsDir);
        Row("Backup folder", ResolvePath(config.BackupPath));
        Row("Trash folder", AppPaths.TrashDir);
        Row("Script trust file", AppPaths.ScriptTrustPath);
        return text.ToString();
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "(not configured)";
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)); }
        catch { return "[invalid location]"; }
    }
}
