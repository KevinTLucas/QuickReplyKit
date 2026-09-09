using System.IO;
using System.Text.Json;
using QuickReplyKit.Core.Library;
using QuickReplyKit.Core.Scripts;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Core.Templates;

namespace QuickReplyKit.Tests;

public class DefaultLibraryExamplesTests
{
    private static readonly string Root = TestFiles.RepoAsset("assets/DefaultLibrary");

    [Fact]
    public void ExampleResponses_AreValid_AndShowEveryBuiltin()
    {
        var examples = LibraryScanner.Scan(Path.Combine(Root, "Examples")).AllResponses;
        var now = new DateTimeOffset(2026, 9, 7, 9, 30, 0, TimeSpan.Zero);

        Assert.Equal(5, examples.Count);
        Assert.All(examples, response => Assert.True(response.IsValid, response.Error));
        string html = Assert.Single(examples, x => x.Name == "Built-in Variables Demo").Html;
        foreach (string token in new[] { "{{greeting}}", "{{date}}", "{{time}}", "{{day}}", "{{date:yyyy-MM-dd}}" })
            Assert.Contains(token, html, StringComparison.Ordinal);
        string resolved = TemplateEngine.Resolve(html, _ => null, now);
        Assert.Contains("Good morning", resolved, StringComparison.Ordinal);
        Assert.Contains(now.ToString("d"), resolved, StringComparison.Ordinal);
        Assert.Contains(now.ToString("t"), resolved, StringComparison.Ordinal);
        Assert.Contains(now.ToString("dddd"), resolved, StringComparison.Ordinal);
        Assert.Contains("2026-09-07", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposedExample_ResolvesBothNamedIncludes()
    {
        var responses = LibraryScanner.Scan(Root).AllResponses;
        var byName = responses.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        string composed = byName["Composed Response Demo"].Html;

        string resolved = TemplateEngine.Resolve(composed,
            name => byName.TryGetValue(name, out var response) ? response.Html : null,
            new DateTimeOffset(2026, 9, 7, 9, 30, 0, TimeSpan.Zero));

        Assert.Contains("thank you for contacting support", resolved, StringComparison.Ordinal);
        Assert.Contains("Please let us know", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void SeededDefinitions_AreValid_Portable_AndMatchResponseTokens()
    {
        string scripts = Path.Combine(Root, "Examples", "Scripts");
        string[] paths = Directory.GetFiles(scripts, "*" + ScriptDefinitionFile.Extension);
        Assert.Single(paths);

        var definitions = paths.Select(path =>
        {
            string json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            Assert.DoesNotContain(document.RootElement.EnumerateObject(),
                property => property.Name.Contains("trust", StringComparison.OrdinalIgnoreCase));
            var result = ScriptDefinitionFile.Load(path);
            Assert.True(result.Success, result.Error);
            return Assert.IsType<ScriptDefinition>(result.Value);
        }).ToArray();

        Assert.All(definitions, x => Assert.Equal(ScriptDefinitionFile.Language, x.Language));
        foreach (var definition in definitions)
        {
            string responsePath = Directory.GetFiles(scripts, "*.json")
                .Single(path => !path.EndsWith(ScriptDefinitionFile.Extension, StringComparison.OrdinalIgnoreCase) &&
                    File.ReadAllText(path).Contains("{{script:" + definition.Name + "}}", StringComparison.Ordinal));
            Assert.True(ResponseFile.Load(responsePath, Root).IsValid);
        }
    }

    [Fact]
    public void RestrictedScriptExample_HasDeterministicOutput()
    {
        string scripts = Path.Combine(Root, "Examples", "Scripts");
        var restricted = Assert.IsType<ScriptDefinition>(ScriptDefinitionFile.Load(
            Path.Combine(scripts, "Example Restricted Greeting" + ScriptDefinitionFile.Extension)).Value);
        Assert.Equal(("Example Restricted Greeting", ScriptDefinitionFile.Language),
            (restricted.Name, restricted.Language));

        var output = ScriptExpression.Evaluate(restricted.Expression,
            new DateTimeOffset(2026, 9, 7, 9, 30, 0, TimeSpan.Zero));
        Assert.True(output.Success, output.Error);
        Assert.Equal("Good morning - 2026-09-07", output.Output);
    }
}
