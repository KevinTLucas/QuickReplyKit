using System.IO;

namespace QuickReplyKit.Core;

/// <summary>
/// Canonical filesystem locations. Library content is user-facing (Documents) so it is
/// easy to browse and share; app config/recent are per-user state (%APPDATA%).
/// </summary>
public static class AppPaths
{
    public const string AppName = "QuickReplyKit";

    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");
    public static string RecentPath => Path.Combine(AppDataDir, "recent.json");

    /// <summary>Default library root; the effective root is configurable in settings.</summary>
    public static string DefaultLibraryDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppName, "Library");

    public static void EnsureAppData() => Directory.CreateDirectory(AppDataDir);
}
