using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        Assert.Equal(7, examples.Count);
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
        Assert.Equal(6, paths.Length);

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

        Assert.Contains(definitions, x => x.Language == ScriptDefinitionFile.Language);
        Assert.Contains(definitions, x => x.Language == ScriptDefinitionFile.NativeLanguage);
        foreach (var definition in definitions)
        {
            string responsePath = Directory.GetFiles(scripts, "*.json")
                .Single(path => !path.EndsWith(ScriptDefinitionFile.Extension, StringComparison.OrdinalIgnoreCase) &&
                    File.ReadAllText(path).Contains("{{script:" + definition.Name + "}}", StringComparison.Ordinal));
            Assert.True(ResponseFile.Load(responsePath, Root).IsValid);
        }
    }

    [Fact]
    public void ScriptExamples_HaveDeterministicRestrictedOutput_AndBoundedNativeScenario()
    {
        string scripts = Path.Combine(Root, "Examples", "Scripts");
        var restricted = Assert.IsType<ScriptDefinition>(ScriptDefinitionFile.Load(
            Path.Combine(scripts, "Example Restricted Greeting" + ScriptDefinitionFile.Extension)).Value);
        var native = Assert.IsType<ScriptDefinition>(ScriptDefinitionFile.Load(
            Path.Combine(scripts, "Example Full-Trust Random Friday Greeting" + ScriptDefinitionFile.Extension)).Value);
        Assert.Equal(("Example Restricted Greeting", ScriptDefinitionFile.Language),
            (restricted.Name, restricted.Language));
        Assert.Equal(("Example Full-Trust Random Friday Greeting", ScriptDefinitionFile.NativeLanguage),
            (native.Name, native.Language));

        var output = ScriptExpression.Evaluate(restricted.Expression,
            new DateTimeOffset(2026, 9, 7, 9, 30, 0, TimeSpan.Zero));
        Assert.True(output.Success, output.Error);
        Assert.Equal("Good morning - 2026-09-07", output.Output);

        string choices = Regex.Match(native.Expression,
            @"\$greetings\s*=\s*@\((.*?)\)\s*\$lines", RegexOptions.Singleline).Groups[1].Value;
        string[] greetings = Regex.Matches(choices, @"'([^']+)'" ).Select(x => x.Groups[1].Value).ToArray();
        Assert.Equal(10, greetings.Length);
        Assert.Equal(10, greetings.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("$env:QRK_NOW", native.Expression, StringComparison.Ordinal);
        Assert.Contains("[DayOfWeek]::Friday", native.Expression, StringComparison.Ordinal);
        Assert.Contains("$lines += 'Happy Friday!'", native.Expression, StringComparison.Ordinal);
        Assert.Contains("[Environment]::NewLine", native.Expression, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"[A-Za-z]:\\", native.Expression);
        Assert.DoesNotContain("USERPROFILE", native.Expression, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$HOME", native.Expression, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(native.Expression.Length, 1, NativeScriptRunner.MaxSourceChars);
    }

    [Fact]
    public async Task AdditionalPowerShellExamples_AreInertPortableAndExecutableAfterApproval()
    {
        string scripts = Path.Combine(Root, "Examples", "Scripts");
        string[] names =
        [
            "Example Next Business Day",
            "Example Business Hours Status",
            "Example Professional Closing",
            "Example Case Note Timestamp",
        ];
        var now = new DateTimeOffset(2026, 9, 11, 18, 30, 0, TimeSpan.FromHours(-5));

        foreach (string name in names)
        {
            var definition = Assert.IsType<ScriptDefinition>(ScriptDefinitionFile.Load(
                Path.Combine(scripts, name + ScriptDefinitionFile.Extension)).Value);
            Assert.False(definition.Enabled);
            Assert.Equal(ScriptDefinitionFile.NativeLanguage, definition.Language);
            Assert.InRange(definition.Expression.Length, 1, NativeScriptRunner.MaxSourceChars);
            Assert.DoesNotMatch(@"(?i)(invoke-webrequest|invoke-restmethod|start-process|new-object\s+net\.|get-content|set-content|\[io\.file\]|registry::)",
                definition.Expression);

            var result = await NativeScriptRunner.EvaluateAsync(definition.Expression, now);
            Assert.True(result.Success, $"{name}: {result.Error}");
            Assert.False(string.IsNullOrWhiteSpace(result.Output));
        }
    }
}
