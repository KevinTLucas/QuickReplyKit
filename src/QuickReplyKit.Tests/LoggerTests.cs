using System.IO;
using QuickReplyKit.Core.Diagnostics;

namespace QuickReplyKit.Tests;

public class LoggerTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "qrk-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    // Wait for the async writer to drain (it flushes when the queue empties). Dispose joins the thread.
    private static string ReadActive(Logger log, string dir)
    {
        log.Dispose();
        return File.ReadAllText(Path.Combine(dir, "quickreplykit.log"));
    }

    [Fact]
    public void LevelGate_DropsMessagesAboveThreshold()
    {
        var dir = TempDir();
        var log = new Logger(dir, LogLevel.Info);
        log.Info("keep-info");
        log.Warn("keep-warn");
        log.Debug("drop-debug");
        log.Verbose("drop-verbose");
        var text = ReadActive(log, dir);

        Assert.Contains("keep-info", text);
        Assert.Contains("keep-warn", text);
        Assert.DoesNotContain("drop-debug", text);
        Assert.DoesNotContain("drop-verbose", text);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void VerboseThreshold_KeepsEverything()
    {
        var dir = TempDir();
        var log = new Logger(dir, LogLevel.Verbose);
        log.Verbose("v"); log.Debug("d"); log.Error("e");
        var text = ReadActive(log, dir);
        Assert.Contains("[VERBO] ", text);
        Assert.Contains("[DEBUG] ", text);
        Assert.Contains("[ERROR] ", text);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Rollover_CapsFileCount_AndKeepsNewest()
    {
        var dir = TempDir();
        // Tiny caps so a few lines force multiple rolls; keep at most 3 files total.
        var log = new Logger(dir, LogLevel.Info, maxBytes: 200, maxFiles: 3);
        for (int i = 0; i < 400; i++) log.Info($"line {i:D4} " + new string('x', 40));
        log.Dispose();

        var files = Directory.GetFiles(dir, "quickreplykit*.log");
        // Active + rolled, never more than maxFiles.
        Assert.True(files.Length <= 3, $"expected ≤3 files, got {files.Length}");
        Assert.Contains(files, f => Path.GetFileName(f) == "quickreplykit.log");
        // The newest lines live in the active file.
        Assert.Contains("line 0399", File.ReadAllText(Path.Combine(dir, "quickreplykit.log")));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void UnwritableDir_DisablesQuietly_NoThrow()
    {
        // A path under an existing FILE can't be a directory → CreateDirectory throws internally,
        // logger disables itself, and logging is a silent no-op (never crashes the caller).
        var file = Path.Combine(Path.GetTempPath(), "qrk-notadir-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "x");
        var log = new Logger(Path.Combine(file, "logs"), LogLevel.Info);
        log.Info("should not throw");     // must not throw
        log.Dispose();
        File.Delete(file);
    }
}
