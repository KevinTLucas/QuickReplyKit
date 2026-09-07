using System.IO;
using System.Text.Json;
using QuickReplyKit.Core.Scripts;

namespace QuickReplyKit.Tests;

public class ScriptDefinitionFileTests
{
    private static ScriptDefinition Valid(string expression = "upper(\"hello\")") =>
        new("script-1", "Greeting", ScriptDefinitionFile.Language, true, expression);

    [Fact]
    public void SaveLoad_RoundTripsExactPortableSchemaWithoutTrust()
    {
        using var temp = new TempDir();
        string path = temp.Of("Greeting" + ScriptDefinitionFile.Extension);

        Assert.True(ScriptDefinitionFile.Save(path, Valid()).Success);
        var loaded = ScriptDefinitionFile.Load(path);

        Assert.True(loaded.Success, loaded.Error);
        Assert.Equal(Valid(), loaded.Value);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(new[] { "id", "name", "language", "enabled", "expression" },
            json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("trust", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"id\":1,\"name\":\"n\",\"language\":\"qrk-expr-1\",\"enabled\":true,\"expression\":\"true\"}")]
    [InlineData("{\"id\":\"i\",\"name\":\"n\",\"language\":\"csharp\",\"enabled\":true,\"expression\":\"true\"}")]
    [InlineData("{\"id\":\"i\",\"name\":\"n\",\"language\":\"qrk-expr-1\",\"enabled\":true,\"expression\":\"true\",\"trusted\":true}")]
    [InlineData("{not json")]
    public void Parse_MalformedWrongLanguageOrTrustField_FailsClosed(string json)
    {
        var result = ScriptDefinitionFile.Parse(json);

        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.StartsWith("[script ", result.Marker);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_RejectsInvalidExpressionAndAllDefinitionBounds()
    {
        string Json(string id, string name, string expression) => JsonSerializer.Serialize(new
        {
            id,
            name,
            language = ScriptDefinitionFile.Language,
            enabled = false,
            expression,
        });

        Assert.False(ScriptDefinitionFile.Parse(Json("", "n", "true")).Success);
        Assert.False(ScriptDefinitionFile.Parse(Json("i", "", "true")).Success);
        Assert.False(ScriptDefinitionFile.Parse(Json(new string('i', ScriptDefinitionFile.MaxIdChars + 1), "n", "true")).Success);
        Assert.False(ScriptDefinitionFile.Parse(Json("i", new string('n', ScriptDefinitionFile.MaxNameChars + 1), "true")).Success);
        Assert.False(ScriptDefinitionFile.Parse(Json("i", "n", "unknown()")).Success);
        Assert.False(ScriptDefinitionFile.Parse(Json("i", "n", new string('x', ScriptExpression.MaxSourceChars + 1))).Success);
        Assert.False(ScriptDefinitionFile.Parse(new string(' ', ScriptDefinitionFile.MaxFileChars + 1)).Success);
    }

    [Fact]
    public void LoadSave_WrongExtensionAndIoFailures_DoNotThrow()
    {
        using var temp = new TempDir();
        Assert.False(ScriptDefinitionFile.Save(temp.Of("script.json"), Valid()).Success);
        Assert.False(ScriptDefinitionFile.Load(temp.Of("script.json")).Success);
        Assert.False(ScriptDefinitionFile.Load(temp.Of("missing" + ScriptDefinitionFile.Extension)).Success);
    }
}
