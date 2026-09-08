using System.IO;

namespace QuickReplyKit.Core;

/// <summary>
/// Canonical filesystem locations. Library content is user-facing (Documents) so it is
/// easy to browse and share; app config/recent are per-user state (%APPDATA%).
/// </summary>
public static class AppPaths
{
    public const string AppName = "QuickReplyKit";

    private static string UserDocumentsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", AppName);

    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");
    public static string RecentPath => Path.Combine(AppDataDir, "recent.json");

    /// <summary>Local-only approvals for sandboxed script definitions.</summary>
    public static string ScriptTrustPath => Path.Combine(AppDataDir, "script-trust.json");

    /// <summary>Rolling diagnostic logs (see <c>Diagnostics.Logger</c>).</summary>
    public static string LogsDir => Path.Combine(AppDataDir, "logs");

    /// <summary>Rolling library snapshots for undo/restore (see <c>ImportExport.LibraryBackup</c>).</summary>
    public static string BackupsDir => Path.Combine(UserDocumentsDir, "Backups");

    /// <summary>App-local soft-delete trash with retention (see <c>Storage.TrashService</c>).</summary>
    public static string TrashDir => Path.Combine(AppDataDir, "trash");

    /// <summary>Default library root; the effective root is configurable in settings.</summary>
    public static string DefaultLibraryDir => Path.Combine(UserDocumentsDir, "Library");

    public static void EnsureAppData() => Directory.CreateDirectory(AppDataDir);
}
