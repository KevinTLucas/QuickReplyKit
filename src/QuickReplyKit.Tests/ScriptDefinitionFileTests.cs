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

    [Fact]
    public void Load_RejectsOversizedFileBeforeReadingItsContents()
    {
        using var temp = new TempDir();
        string path = temp.Of("huge" + ScriptDefinitionFile.Extension);
        using (var file = File.Create(path)) file.SetLength(ScriptDefinitionFile.MaxFileBytes + 1L);

        var result = ScriptDefinitionFile.Load(path);

        Assert.False(result.Success);
        Assert.Equal("[script definition error: file-limit]", result.Marker);
    }

    [Fact]
    public void Save_MaximallyEscapedValidDefinitionAlwaysLoadsAndLeavesNoTempFile()
    {
        using var temp = new TempDir();
        string path = temp.Of("large" + ScriptDefinitionFile.Extension);
        string expression = "concat(" + string.Join(',', Enumerable.Repeat("\"" + new string('é', 1_000) + "\"", 4)) + ")";
        var definition = new ScriptDefinition(
            new string('é', ScriptDefinitionFile.MaxIdChars),
            new string('é', ScriptDefinitionFile.MaxNameChars),
            ScriptDefinitionFile.Language, true, expression);

        var saved = ScriptDefinitionFile.Save(path, definition);
        var loaded = ScriptDefinitionFile.Load(path);

        Assert.True(saved.Success, saved.Error);
        Assert.True(loaded.Success, loaded.Error);
        Assert.Equal(definition, loaded.Value);
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.tmp"));
    }
}
