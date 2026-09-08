using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using QuickReplyKit.Core.Diagnostics;

namespace QuickReplyKit.Core.Scripts;

/// <summary>Runs explicitly trusted Windows PowerShell definitions outside this process.</summary>
public static class NativeScriptRunner
{
    public const int MaxSourceChars = 4_096;
    public const int MaxOutputChars = 8_192;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static readonly SemaphoreSlim Slots = new(2, 2);

    public static ScriptOperationResult Validate(string? source) => source switch
    {
        null or "" => ScriptOperationResult.Fail("", "invalid-script", "The script is blank."),
        { Length: > MaxSourceChars } => ScriptOperationResult.Fail("", "source-limit",
            $"Source exceeds {MaxSourceChars} characters."),
        _ => ScriptOperationResult.Ok(),
    };

    public static Task<ScriptEvaluationResult> EvaluateAsync(string? source, DateTimeOffset now,
        string? libraryRoot = null, CancellationToken cancellationToken = default) =>
        EvaluateAsync(source, now, libraryRoot, cancellationToken, PowerShellPath(), Timeout);

    internal static async Task<ScriptEvaluationResult> EvaluateAsync(string? source, DateTimeOffset now,
        string? libraryRoot, CancellationToken cancellationToken, string executablePath, TimeSpan timeout)
    {
        var valid = Validate(source);
        if (!valid.Success) return new(false, valid.Marker, valid.Error);

        var timer = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(timeout);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, deadline.Token);
        bool entered = false;
        try
        {
            await Slots.WaitAsync(stop.Token).ConfigureAwait(false);
            entered = true;
            if (!File.Exists(executablePath)) return Finish("runtime-unavailable", timer);

            using var process = new Process { StartInfo = StartInfo(executablePath, libraryRoot, now) };
            try
            {
                if (!process.Start()) return Finish("runtime-unavailable", timer);
            }
            catch
            {
                return Finish("runtime-unavailable", timer);
            }

            int outputExceeded = 0;
            Task<string> output = CaptureOutput(process.StandardOutput, process,
                () => Interlocked.Exchange(ref outputExceeded, 1), stop.Token);
            Task errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, stop.Token);

            try
            {
                await process.StandardInput.WriteLineAsync(
                    "$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)")
                    .ConfigureAwait(false);
                await process.StandardInput.WriteAsync(source).ConfigureAwait(false);
                process.StandardInput.Close();

                await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);
                string text = await output.ConfigureAwait(false);
                await errors.ConfigureAwait(false);

                if (outputExceeded != 0 || WebUtility.HtmlEncode(text).Length > MaxOutputChars)
                    return Finish("output-limit", timer, process.ExitCode);
                if (process.ExitCode != 0)
                    return Finish("process-failed", timer, process.ExitCode);
                return Finish(ScriptEvaluationResult.Ok(WebUtility.HtmlEncode(text)), "success", timer,
                    process.ExitCode);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                await Ignore(output).ConfigureAwait(false);
                await Ignore(errors).ConfigureAwait(false);
                return Finish(cancellationToken.IsCancellationRequested ? "canceled" : "timeout", timer);
            }
            catch
            {
                Kill(process);
                await Ignore(output).ConfigureAwait(false);
                await Ignore(errors).ConfigureAwait(false);
                return outputExceeded != 0
                    ? Finish("output-limit", timer)
                    : Finish("process-failed", timer);
            }
        }
        catch (OperationCanceledException)
        {
            return Finish(cancellationToken.IsCancellationRequested ? "canceled" : "timeout", timer);
        }
        catch
        {
            return Finish("process-failed", timer);
        }
        finally
        {
            if (entered) Slots.Release();
        }
    }

    private static ProcessStartInfo StartInfo(string executablePath, string? libraryRoot,
        DateTimeOffset now)
    {
        var start = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("-");
        start.Environment["QRK_LIBRARY_ROOT"] = libraryRoot ?? "";
        start.Environment["QRK_NOW"] = now.ToString("O", CultureInfo.InvariantCulture);
        return start;
    }

    private static async Task<string> CaptureOutput(StreamReader reader, Process process,
        Action exceeded, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[512];
        while (true)
        {
            int read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) return result.ToString();
            if (result.Length + read > MaxOutputChars)
            {
                exceeded();
                Kill(process);
                return "";
            }
            result.Append(buffer, 0, read);
        }
    }

    private static string PowerShellPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { }
    }

    private static async Task Ignore(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
        catch { }
    }

    private static ScriptEvaluationResult Finish(string code, Stopwatch timer, int? exitCode = null) =>
        Finish(ScriptEvaluationResult.Fail(code, SafeError(code)), code, timer, exitCode);

    private static ScriptEvaluationResult Finish(ScriptEvaluationResult result, string outcome,
        Stopwatch timer, int? exitCode = null)
    {
        Diag.Verbose("NativeScriptRunner", "ScriptRuntime",
            $"language={ScriptDefinitionFile.NativeLanguage} outcome={outcome} exitCode={exitCode?.ToString(CultureInfo.InvariantCulture) ?? "none"} elapsedMs={timer.ElapsedMilliseconds}");
        return result;
    }

    private static string SafeError(string code) => code switch
    {
        "runtime-unavailable" => "Windows PowerShell is unavailable.",
        "timeout" => "The script exceeded its time limit.",
        "output-limit" => $"The script output exceeds {MaxOutputChars} characters.",
        "canceled" => "The script was canceled.",
        _ => "The script process failed.",
    };
}
