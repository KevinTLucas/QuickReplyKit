using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace QuickReplyKit.Core.Diagnostics;

public enum LogLevel { Error = 0, Warn = 1, Info = 2, Debug = 3, Verbose = 4 }

/// <summary>
/// Small thread-safe, size-capped rolling logger — no NuGet (Serilog/NLog are banned here). Writes are
/// queued to one background thread so callers never block on I/O; the level gate drops filtered
/// messages before they queue. When the active file passes <c>maxBytes</c> it rolls
/// (<c>quickreplykit.log → quickreplykit.1.log → …</c>, keeping <c>maxFiles</c>, ~50 MB total).
/// If the log directory is unwritable the logger silently disables itself — logging must never crash
/// the app it is trying to diagnose.
/// </summary>
public sealed class Logger : IDisposable
{
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly string _dir;
    private readonly string _path;
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>());
    private readonly Thread _worker;
    private StreamWriter? _writer;
    private long _written;
    private volatile bool _enabled;

    /// <summary>Messages above this level are dropped (Info = normal, Verbose = everything).</summary>
    public LogLevel Threshold { get; set; } = LogLevel.Info;

    public string Directory => _dir;

    // maxBytes/maxFiles default to ~50 MB total; overridable so tests can force a roll cheaply.
    public Logger(string dir, LogLevel threshold = LogLevel.Info,
        long maxBytes = 10 * 1024 * 1024, int maxFiles = 5)
    {
        _dir = dir;
        _path = Path.Combine(dir, "quickreplykit.log");
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
        Threshold = threshold;
        try { System.IO.Directory.CreateDirectory(dir); OpenWriter(); _enabled = true; }
        catch { _enabled = false; }
        _worker = new Thread(Drain) { IsBackground = true, Name = "QRK-Logger" };
        _worker.Start();
    }

    public void Log(LogLevel level, string message, Exception? ex = null)
    {
        if (!_enabled || level > Threshold) return;
        var sb = new StringBuilder(message.Length + 48);
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
          .Append(" [").Append(Name(level)).Append("] [T")
          .Append(Environment.CurrentManagedThreadId).Append("] ").Append(message);
        if (ex != null) sb.Append(Environment.NewLine).Append(ex);
        try { _queue.Add(sb.ToString()); } catch (InvalidOperationException) { /* completed */ }
    }

    public void Error(string m, Exception? ex = null) => Log(LogLevel.Error, m, ex);
    public void Warn(string m) => Log(LogLevel.Warn, m);
    public void Info(string m) => Log(LogLevel.Info, m);
    public void Debug(string m) => Log(LogLevel.Debug, m);
    public void Verbose(string m) => Log(LogLevel.Verbose, m);

    private static string Name(LogLevel l) => l switch
    {
        LogLevel.Error => "ERROR",
        LogLevel.Warn => "WARN ",
        LogLevel.Info => "INFO ",
        LogLevel.Debug => "DEBUG",
        _ => "VERBO",
    };

    private void OpenWriter()
    {
        var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = false };
        _written = fs.Length;
    }

    private void Drain()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            if (_writer is null) continue;
            try
            {
                _writer.WriteLine(line);
                _written += line.Length + Environment.NewLine.Length;
                if (_queue.Count == 0) _writer.Flush();   // batch flushes; always flush when caught up
                if (_written >= _maxBytes) Roll();
            }
            catch { /* best effort — never let logging throw */ }
        }
        try { _writer?.Flush(); _writer?.Dispose(); } catch { /* shutting down */ }
    }

    private void Roll()
    {
        try
        {
            _writer!.Flush(); _writer.Dispose(); _writer = null;
            string Rolled(int i) => Path.Combine(_dir, $"quickreplykit.{i}.log");
            var oldest = Rolled(_maxFiles - 1);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int i = _maxFiles - 2; i >= 1; i--)
                if (File.Exists(Rolled(i))) File.Move(Rolled(i), Rolled(i + 1), overwrite: true);
            if (File.Exists(_path)) File.Move(_path, Rolled(1), overwrite: true);
            OpenWriter();
        }
        catch { _enabled = false; }   // give up quietly rather than spin on a broken FS
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        try { _worker.Join(2000); } catch { /* ignore */ }
    }
}
