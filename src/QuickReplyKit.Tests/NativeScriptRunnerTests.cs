using System.IO;
using System.Net;
using System.Text.Json;
using QuickReplyKit.Core.Scripts;

namespace QuickReplyKit.Tests;

public class NativeScriptRunnerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 14, 5, 6, TimeSpan.FromHours(-5));

    private static string PowerShell => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public void DefinitionSchema_RoundTripsBothLanguages_AndOldLanguageRemainsUnchanged()
    {
        using var temp = new TempDir();
        var expression = new ScriptDefinition("old", "Old", ScriptDefinitionFile.Language,
            true, "\"hello\"");
        var native = new ScriptDefinition("native", "Native", ScriptDefinitionFile.NativeLanguage,
            true, "Write-Output 'hello'");

        foreach (var definition in new[] { expression, native })
        {
            string path = temp.Of(definition.Id + ScriptDefinitionFile.Extension);
            Assert.True(ScriptDefinitionFile.Save(path, definition).Success);
            Assert.Equal(definition, ScriptDefinitionFile.Load(path).Value);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(new[] { "id", "name", "language", "enabled", "expression" },
                json.RootElement.EnumerateObject().Select(property => property.Name));
        }

        Assert.Equal("qrk-expr-1", ScriptDefinitionFile.Language);
        Assert.False(ScriptDefinitionFile.Validate(native with
        {
            Expression = new string('x', NativeScriptRunner.MaxSourceChars + 1),
        }).Success);
    }

    [Fact]
    public void Trust_RemainsBoundToExactLanguageAndSource()
    {
        using var temp = new TempDir();
        var ledger = ScriptTrustLedger.Open(temp.Of("trust.json")).Value!;
        var native = new ScriptDefinition("same-id", "Greeting",
            ScriptDefinitionFile.NativeLanguage, true, "Write-Output 'hello'");

        Assert.True(ledger.Trust(native).Success);
        Assert.True(ledger.IsTrusted(native));
        Assert.False(ledger.IsTrusted(native with { Expression = "Write-Output 'changed'" }));
        Assert.NotEqual(ScriptTrustLedger.ComputeFingerprint(native),
            ScriptTrustLedger.ComputeFingerprint(native with { Language = ScriptDefinitionFile.Language }));
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsHtmlEncodedStdoutAndFixedEnvironmentOnlyAsOutput()
    {
        using var temp = new TempDir();
        string root = temp.Of("Library & One");
        Directory.CreateDirectory(root);
        string source = "[Console]::Out.Write('<&|' + $env:QRK_NOW + '|' + $env:QRK_LIBRARY_ROOT)";

        var result = await NativeScriptRunner.EvaluateAsync(source, Now, root);

        Assert.True(result.Success, result.Error);
        Assert.Equal(WebUtility.HtmlEncode($"<&|{Now:O}|{root}"), result.Output);
    }

    [Fact]
    public async Task EvaluateAsync_StderrAndNonzeroExitReturnGenericProcessFailure()
    {
        var result = await NativeScriptRunner.EvaluateAsync(
            "[Console]::Error.Write('customer-secret'); exit 7", Now);

        Assert.False(result.Success);
        Assert.Equal("[script error: process-failed]", result.Output);
        Assert.DoesNotContain("customer-secret", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_MissingRuntimeReturnsStableMarker()
    {
        var result = await NativeScriptRunner.EvaluateAsync("'ok'", Now, null,
            CancellationToken.None, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"),
            TimeSpan.FromSeconds(1));

        Assert.Equal("[script error: runtime-unavailable]", result.Output);
    }

    [Fact]
    public async Task EvaluateAsync_TimeoutKillsAndReturnsStableMarker()
    {
        var result = await NativeScriptRunner.EvaluateAsync("Start-Sleep -Seconds 10", Now, null,
            CancellationToken.None, PowerShell, TimeSpan.FromMilliseconds(250));

        Assert.Equal("[script error: timeout]", result.Output);
    }

    [Fact]
    public async Task EvaluateAsync_OutputLimitKillsAndReturnsStableMarker()
    {
        var result = await NativeScriptRunner.EvaluateAsync(
            "[Console]::Out.Write('x' * 9000)", Now);

        Assert.Equal("[script error: output-limit]", result.Output);
    }

    [Fact]
    public async Task EvaluateAsync_CallerCancellationKillsAndReturnsStableMarker()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var result = await NativeScriptRunner.EvaluateAsync("Start-Sleep -Seconds 10", Now, null,
            stop.Token, PowerShell, TimeSpan.FromSeconds(5));

        Assert.Equal("[script error: canceled]", result.Output);
    }

    [Fact]
    public async Task Catalog_DispatchesTrustedNativeScriptAndProvidesLibraryRoot()
    {
        using var temp = new TempDir();
        string root = temp.Of("lib");
        var definition = new ScriptDefinition("native", "Native",
            ScriptDefinitionFile.NativeLanguage, true,
            "[Console]::Out.Write('<' + $env:QRK_LIBRARY_ROOT + '>')");
        Assert.True(ScriptDefinitionFile.Save(
            Path.Combine(root, "Native" + ScriptDefinitionFile.Extension), definition).Success);
        var trust = ScriptTrustLedger.Open(temp.Of("trust.json")).Value!;
        Assert.True(trust.Trust(definition).Success);

        var result = await ScriptCatalog.Load(root, trust).EvaluateAsync("native", Now);

        Assert.True(result.Success, result.Error);
        Assert.Equal(WebUtility.HtmlEncode($"<{root}>"), result.Output);
    }
}
