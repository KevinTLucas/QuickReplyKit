using System.IO;
using System.Security.Cryptography;
using System.Text;
using QuickReplyKit.Core.Scripts;

namespace QuickReplyKit.Tests;

public class ScriptTrustLedgerTests
{
    private static ScriptDefinition Definition(string expression = "upper(\"hello\")") =>
        new("stable-id", "Greeting", ScriptDefinitionFile.Language, true, expression);

    [Fact]
    public void Trust_PersistsByIdAndCodeFingerprint()
    {
        using var temp = new TempDir();
        string path = temp.Of("script-trust.json");
        var opened = ScriptTrustLedger.Open(path);
        Assert.True(opened.Success, opened.Error);

        Assert.False(opened.Value!.IsTrusted(Definition()));
        Assert.True(opened.Value.Trust(Definition()).Success);
        Assert.True(opened.Value.IsTrusted(Definition()));

        var reloaded = ScriptTrustLedger.Open(path);
        Assert.True(reloaded.Success, reloaded.Error);
        Assert.True(reloaded.Value!.IsTrusted(Definition()));
    }

    [Fact]
    public void Trust_CodeOrLanguageChangeInvalidatesButMetadataChangeDoesNot()
    {
        using var temp = new TempDir();
        var ledger = ScriptTrustLedger.Open(temp.Of("trust.json")).Value!;
        Assert.True(ledger.Trust(Definition()).Success);

        Assert.False(ledger.IsTrusted(Definition("lower(\"hello\")")));
        Assert.False(ledger.IsTrusted(Definition() with { Language = "qrk-expr-2" }));
        Assert.True(ledger.IsTrusted(Definition() with { Name = "Renamed", Enabled = false }));
        Assert.False(ledger.IsTrusted(Definition() with { Id = "different-id" }));
    }

    [Fact]
    public void Fingerprint_IsExactlySha256OfLanguageNewlineExpression()
    {
        var definition = Definition();
        string expected = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(definition.Language + "\n" + definition.Expression)));

        Assert.Equal(expected, ScriptTrustLedger.ComputeFingerprint(definition));
    }

    [Fact]
    public void Revoke_RemovesTrustAndPersists()
    {
        using var temp = new TempDir();
        string path = temp.Of("trust.json");
        var ledger = ScriptTrustLedger.Open(path).Value!;
        ledger.Trust(Definition());

        Assert.True(ledger.Revoke(Definition().Id).Success);
        Assert.False(ledger.IsTrusted(Definition()));
        Assert.False(ScriptTrustLedger.Open(path).Value!.IsTrusted(Definition()));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"version\":1,\"trusted\":{\"id\":\"not-a-hash\"}}")]
    public void Open_CorruptLedgerFailsClosedWithoutTrust(string content)
    {
        using var temp = new TempDir();
        string path = temp.Of("trust.json");
        File.WriteAllText(path, content);

        var result = ScriptTrustLedger.Open(path);

        Assert.False(result.Success);
        Assert.StartsWith("[script trust error:", result.Marker);
        Assert.False(result.Value!.IsTrusted(Definition()));
    }

    [Fact]
    public void InvalidDefinitionAndIoFailure_ReturnErrorsWithoutMutationOrThrowing()
    {
        using var temp = new TempDir();
        var ledger = ScriptTrustLedger.Open(temp.Of("trust.json")).Value!;
        Assert.False(ledger.Trust(Definition() with { Expression = "eval(\"x\")" }).Success);
        Assert.False(ledger.IsTrusted(Definition() with { Expression = null! }));
        Assert.False(ledger.Revoke("").Success);
        Assert.False(ScriptTrustLedger.Open(null!).Success);

        string directoryAsFile = temp.Of("blocked");
        File.WriteAllText(directoryAsFile, "x");
        var blocked = ScriptTrustLedger.Open(Path.Combine(directoryAsFile, "trust.json")).Value!;
        Assert.False(blocked.Trust(Definition()).Success);
        Assert.False(blocked.IsTrusted(Definition()));
    }
}
