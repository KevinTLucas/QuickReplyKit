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
    public void ReportUsesActualState_AndOmitsResponseBindingDetails()
    {
        using var temp = new TempDir();
        string secret = "PRIVATE-" + Guid.NewGuid().ToString("N");
        string scannedLibrary = temp.Of("library");
        TestFiles.WriteResponse(Path.Combine(scannedLibrary, secret + ".json"), $"<p>{secret}</p>", secret);
        File.WriteAllText(Path.Combine(scannedLibrary, "broken.json"), "{");
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var config = new AppConfig
        {
            LibraryPath = Path.Combine(profile, secret, "Responses"),
            BackupPath = @"\\server\" + secret + @"\backups",
            HotkeyGesture = "Ctrl+Alt+D",
            VerboseLogging = false,
            ResponseHotkeys =
            {
                new() { RelativePath = secret + ".json", Gesture = "Ctrl+Alt+1" },
                new() { RelativePath = "missing.json", Gesture = "Ctrl+Alt+2" },
            },
        };
        using var library = new LibraryService(scannedLibrary, recentPath: temp.Of("recent.json"), watch: false);
        var entries = new[]
        {
            new HotkeyService.Entry("Ctrl+Alt+D", true, null, false, true),
            new HotkeyService.Entry("Ctrl+Alt+1", false, secret + ".json", false, true),
            new HotkeyService.Entry("Ctrl+Alt+2", false, "missing.json", false, false),
        };

        Diag.Init(temp.Of("logs"), verbose: true);
        try
        {
            string report = DiagnosticsReport.Build(config, library, entries, "1.2.3");
            Assert.Contains("1.2.3", report);
            Assert.Contains("Ctrl+Alt+D (registered)", report);
            Assert.Contains("2 configured, 1 registered, 1 failed", report);
            Assert.Contains("2 (1 valid, 1 invalid)", report);
            Assert.Contains("Watcher               : not attached", report);
            Assert.Contains("Logging               : active (verbose)", report);
            Assert.Contains("Trash folder", report);
            Assert.Equal(@"%USERPROFILE%\[private path]", DiagnosticsReport.Redact(Path.Combine(profile, "Documents", "QRK")));
            Assert.Equal("%USERPROFILE%", DiagnosticsReport.Redact(profile));
            Assert.Equal(@"%USERPROFILE%\[private path]",
                DiagnosticsReport.Redact(@"C:\Users\KEVIN_~1\AppData\Local\Temp\QRK"));
            Assert.Equal("[custom location]", DiagnosticsReport.Redact(@"D:\Customer Name\Shared Responses"));
            Assert.Equal("[network location]", DiagnosticsReport.Redact(@"\\server\customer\responses"));
            Assert.DoesNotContain(secret, report, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Ctrl+Alt+1", report);
            Assert.DoesNotContain("Ctrl+Alt+2", report);
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
    }
}
