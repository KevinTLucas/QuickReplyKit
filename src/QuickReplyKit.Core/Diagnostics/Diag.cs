namespace QuickReplyKit.Core.Diagnostics;

/// <summary>
/// Ambient facade over the process-wide <see cref="Logger"/> so call sites can just write
/// <c>Diag.Info("…")</c> without threading a logger through everything. No-ops until
/// <see cref="Init"/> runs (and after <see cref="Shutdown"/>), so logging is always safe to call.
/// </summary>
public static class Diag
{
    private static Logger? _logger;

    public static void Init(string dir, bool verbose) =>
        _logger = new Logger(dir, verbose ? LogLevel.Verbose : LogLevel.Info);

    public static void SetVerbose(bool verbose)
    {
        if (_logger is not null) _logger.Threshold = verbose ? LogLevel.Verbose : LogLevel.Info;
    }

    public static string? Directory => _logger?.Directory;

    public static void Error(string m, Exception? ex = null) => _logger?.Error(m, ex);
    public static void Warn(string m) => _logger?.Warn(m);
    public static void Info(string m) => _logger?.Info(m);
    public static void Debug(string m) => _logger?.Debug(m);
    public static void Verbose(string m) => _logger?.Verbose(m);

    public static void Shutdown() { _logger?.Dispose(); _logger = null; }
}
