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
    public static bool IsEnabled => _logger?.IsEnabled == true;
    public static LogLevel? Threshold => _logger?.Threshold;

    public static void Error(string source, string category, string message, Exception? ex = null) =>
        _logger?.Error(source, category, message, ex);
    public static void Warn(string source, string category, string message, Exception? ex = null) =>
        _logger?.Warn(source, category, message, ex);
    public static void Info(string source, string category, string message) => _logger?.Info(source, category, message);
    public static void Debug(string source, string category, string message) => _logger?.Debug(source, category, message);
    public static void Verbose(string source, string category, string message) => _logger?.Verbose(source, category, message);

    public static void Shutdown() { _logger?.Dispose(); _logger = null; }
}
