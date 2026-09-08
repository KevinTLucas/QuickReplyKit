using System.IO;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Services;

namespace QuickReplyKit.Tests;

[Collection("Diagnostics")]
public class DiagnosticsReportTests
{
    [Fact]
    public void ReportUsesActualStateAndResolvedPaths_WithoutResponseContent()
    {
        using var temp = new TempDir();
        string contentSecret = "PRIVATE-" + Guid.NewGuid().ToString("N");
        string scannedLibrary = temp.Of("library");
        string configuredLibrary = temp.Of("configured-library");
        string configuredBackups = temp.Of("configured-backups");
        TestFiles.WriteResponse(Path.Combine(scannedLibrary, contentSecret + ".json"), $"<p>{contentSecret}</p>", contentSecret);
        File.WriteAllText(Path.Combine(scannedLibrary, "broken.json"), "{");

        var config = new AppConfig
        {
            LibraryPath = configuredLibrary,
            BackupPath = configuredBackups,
            HotkeyGesture = "SECRET-LAUNCHER-HOTKEY",
            VerboseLogging = false,
            ResponseHotkeys =
            {
                new() { RelativePath = contentSecret + ".json", Gesture = "Ctrl+Alt+1" },
                new() { RelativePath = "missing.json", Gesture = "Ctrl+Alt+2" },
            },
        };
        using var library = new LibraryService(scannedLibrary, recentPath: temp.Of("recent.json"), watch: false);
        var entries = new[]
        {
            new HotkeyService.Entry("Ctrl+Alt+D", true, null, false, true),
            new HotkeyService.Entry("Ctrl+Alt+1", false, contentSecret + ".json", false, true),
            new HotkeyService.Entry("Ctrl+Alt+2", false, "missing.json", false, false),
        };

        Diag.Init(temp.Of("logs"), verbose: true);
        try
        {
            string report = DiagnosticsReport.Build(config, library, entries, "1.2.3");
            Assert.Contains("1.2.3", report);
            Assert.Contains("Launcher hotkey       : registered", report);
            Assert.Contains("2 configured, 1 registered, 1 failed", report);
            Assert.Contains("2 (1 valid, 1 invalid)", report);
            Assert.Contains("Watcher               : not attached", report);
            Assert.Contains("Logging               : active (verbose)", report);
            Assert.Contains("Trash folder", report);
            Assert.Contains("Scripts               : 0 total, 0 enabled, 0 trusted, 0 invalid", report);
            Assert.Contains("Library folder        : " + Path.GetFullPath(configuredLibrary), report);
            Assert.Contains("Config file           : " + AppPaths.ConfigPath, report);
            Assert.Contains("Logs folder           : " + AppPaths.LogsDir, report);
            Assert.Contains("Backup folder         : " + Path.GetFullPath(configuredBackups), report);
            Assert.Contains("Trash folder          : " + AppPaths.TrashDir, report);
            Assert.Contains("Script trust file     : " + AppPaths.ScriptTrustPath, report);
            Assert.DoesNotContain("%APPDATA%", report);
            Assert.DoesNotContain("%USERPROFILE%", report);
            Assert.DoesNotContain("[private path]", report);
            Assert.DoesNotContain(contentSecret, report, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Ctrl+Alt+1", report);
            Assert.DoesNotContain("Ctrl+Alt+2", report);
            Assert.DoesNotContain("SECRET-LAUNCHER-HOTKEY", report);
        }
        finally { Diag.Shutdown(); }
    }

    [Fact]
    public void ReportShowsUnavailableLogger()
    {
        using var temp = new TempDir();
        using var library = new LibraryService(temp.Of("library"), recentPath: temp.Of("recent.json"), watch: false);
        Diag.Shutdown();
        string report = DiagnosticsReport.Build(new AppConfig(), library, Array.Empty<HotkeyService.Entry>(), "test");
        Assert.Contains("Logging               : unavailable", report);
        Assert.Contains("Library folder        : " + AppPaths.DefaultLibraryDir, report);
        Assert.Contains("Config file           : " + AppPaths.ConfigPath, report);
        Assert.Contains("Logs folder           : " + AppPaths.LogsDir, report);
        Assert.Contains("Backup folder         : " + AppPaths.BackupsDir, report);
        Assert.Contains("Trash folder          : " + AppPaths.TrashDir, report);
        Assert.Contains("Script trust file     : " + AppPaths.ScriptTrustPath, report);
        Assert.DoesNotContain("%APPDATA%", report);
        Assert.DoesNotContain("%USERPROFILE%", report);
        Assert.DoesNotContain("[private path]", report);
    }

    [Fact]
    public void ReportExpandsEnvironmentPaths_AndHandlesInvalidPaths()
    {
        using var temp = new TempDir();
        using var library = new LibraryService(temp.Of("library"), recentPath: temp.Of("recent.json"), watch: false);
        var config = new AppConfig
        {
            LibraryPath = @"%USERPROFILE%\QuickReplyKit-QA\Library",
            BackupPath = "\0",
        };

        string report = DiagnosticsReport.Build(config, library, Array.Empty<HotkeyService.Entry>(), "test");

        Assert.Contains("Library folder        : " + Path.GetFullPath(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "QuickReplyKit-QA", "Library")), report);
        Assert.Contains("Backup folder         : [invalid location]", report);
    }
}
