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
        Assert.True(log.IsEnabled);
        log.Info("Test", "Gate", "keep-info");
        log.Warn("Test", "Gate", "keep-warn");
        log.Debug("Test", "Gate", "drop-debug");
        log.Verbose("Test", "Gate", "drop-verbose");
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
        log.Verbose("Test", "Levels", "v");
        log.Debug("Test", "Levels", "d");
        log.Error("Test", "Levels", "e");
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
        var log = new Logger(dir, LogLevel.Info, maxBytes: 250, maxFiles: 3);
        for (int i = 0; i < 400; i++) log.Info("Test", "Rollover", $"line {i:D4} " + new string('x', 40));
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
        Assert.False(log.IsEnabled);
        log.Info("Test", "Filesystem", "should not throw");     // must not throw
        log.Dispose();
        File.Delete(file);
    }

    [Fact]
    public void RolloverFailure_MarksLoggerUnavailable()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "quickreplykit.1.log"));
        var log = new Logger(dir, LogLevel.Info, maxBytes: 1, maxFiles: 2);
        log.Info("Test", "Filesystem", "force rollover");

        Assert.True(SpinWait.SpinUntil(() => !log.IsEnabled, 5000));
        log.Dispose();
        Directory.Delete(dir, true);
    }

    [Fact]
    public void StructuredMetadata_AndSafeException_AreWrittenWithoutExceptionMessage()
    {
        var dir = TempDir();
        string canary = "PRIVATE-" + Guid.NewGuid().ToString("N");
        var log = new Logger(dir, LogLevel.Info);

        Exception exception;
        try { throw new IOException(canary, new InvalidDataException(canary)); }
        catch (Exception ex) { exception = ex; }
        log.Error("PasteEngine\r\nFORGED", "Clipboard\nFORGED", "restore failed\nFORGED", exception);
        var text = ReadActive(log, dir);

        Assert.Contains("[PasteEngine  FORGED/Clipboard FORGED] restore failed FORGED", text);
        Assert.Contains("exceptionType=System.IO.IOException", text);
        Assert.Contains("exceptionType=System.IO.InvalidDataException", text);
        Assert.Contains("hresult=0x", text);
        Assert.Contains("QuickReplyKit.Tests.LoggerTests", text);
        Assert.DoesNotContain(canary, text);
        Assert.DoesNotContain("\nFORGED", text);
        Directory.Delete(dir, true);
    }
}
