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
    private const int MaxErrorChars = 4_096;
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
                if (!process.Start()) return Finish("runtime-blocked", timer);
            }
            catch
            {
                return Finish("runtime-blocked", timer);
            }

            int outputExceeded = 0;
            int errorExceeded = 0;
            Task<string> output = Capture(process.StandardOutput, MaxOutputChars, process,
                () => Interlocked.Exchange(ref outputExceeded, 1), stop.Token);
            Task<string> errors = Capture(process.StandardError, MaxErrorChars, process,
                () => Interlocked.Exchange(ref errorExceeded, 1), stop.Token);

            try
            {
                await process.StandardInput.WriteAsync(source).ConfigureAwait(false);
                process.StandardInput.Close();
                await process.WaitForExitAsync(stop.Token).ConfigureAwait(false);
                string text = await output.ConfigureAwait(false);
                string error = await errors.ConfigureAwait(false);
                string encoded = EncodeOutput(text);

                if (outputExceeded != 0 || encoded.Length > MaxOutputChars)
                    return Finish("output-limit", timer, process.ExitCode);
                if (errorExceeded != 0)
                    return Finish("error-output-limit", timer, process.ExitCode);
                if (process.ExitCode != 0)
                    return Finish("process-failed", timer, process.ExitCode, error);
                return Finish(ScriptEvaluationResult.Ok(encoded), "success", timer,
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
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; " +
            "$OutputEncoding=[Console]::InputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); " +
            "& ([ScriptBlock]::Create([Console]::In.ReadToEnd()))");
        start.Environment["QRK_LIBRARY_ROOT"] = libraryRoot ?? "";
        start.Environment["QRK_NOW"] = now.ToString("O", CultureInfo.InvariantCulture);
        return start;
    }

    private static async Task<string> Capture(StreamReader reader, int limit, Process process,
        Action exceeded, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[512];
        while (true)
        {
            int read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) return result.ToString();
            if (result.Length + read > limit)
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

    private static ScriptEvaluationResult Finish(string code, Stopwatch timer, int? exitCode = null,
        string? standardError = null) =>
        Finish(ScriptEvaluationResult.Fail(code, SafeError(code, standardError)), code, timer, exitCode);

    private static ScriptEvaluationResult Finish(ScriptEvaluationResult result, string outcome,
        Stopwatch timer, int? exitCode = null)
    {
        Diag.Verbose("NativeScriptRunner", "ScriptRuntime",
            $"language={ScriptDefinitionFile.NativeLanguage} outcome={outcome} exitCode={exitCode?.ToString(CultureInfo.InvariantCulture) ?? "none"} elapsedMs={timer.ElapsedMilliseconds}");
        return result;
    }

    private static string SafeError(string code, string? standardError = null) => code switch
    {
        "runtime-unavailable" => "Windows PowerShell is unavailable.",
        "runtime-blocked" => "Windows PowerShell could not start. It may be blocked by organizational application-control policy.",
        "timeout" => "The script exceeded its time limit.",
        "output-limit" => $"The script output exceeds {MaxOutputChars} characters.",
        "error-output-limit" => "The script produced too much error output.",
        "canceled" => "The script was canceled.",
        _ when Contains(standardError, "running scripts is disabled") ||
               Contains(standardError, "not digitally signed") =>
            "Windows PowerShell blocked the script because of the current execution policy.",
        _ when Contains(standardError, "access is denied") ||
               Contains(standardError, "unauthorizedaccess") ||
               Contains(standardError, "permissiondenied") =>
            "Windows PowerShell was denied access. Scripts run as the signed-in user; check that user's file, network, registry, and command permissions.",
        _ when Contains(standardError, "is not recognized as the name") =>
            "A command used by the PowerShell script is unavailable on this computer.",
        _ => "Windows PowerShell returned an error. Review the script in Script Manager and try again.",
    };

    private static bool Contains(string? value, string text) =>
        value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;

    private static string EncodeOutput(string text) => WebUtility.HtmlEncode(text.TrimEnd('\r', '\n'))
        .Replace("\r\n", "<br>", StringComparison.Ordinal)
        .Replace("\r", "<br>", StringComparison.Ordinal)
        .Replace("\n", "<br>", StringComparison.Ordinal);
}
