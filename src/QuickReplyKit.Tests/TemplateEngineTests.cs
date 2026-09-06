using System.Globalization;
using QuickReplyKit.Core.Templates;

namespace QuickReplyKit.Tests;

public class TemplateEngineTests
{
    // Thursday, 2026-01-15. Three copies across the greeting bands, all offset-zero for determinism.
    private static readonly DateTimeOffset Morning = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Afternoon = new(2026, 1, 15, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Evening = new(2026, 1, 15, 19, 45, 0, TimeSpan.Zero);

    private static Func<string, string?> None => static _ => null;

    private static Func<string, string?> Lookup(Dictionary<string, string> map)
    {
        var d = new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        return name => d.TryGetValue(name, out var html) ? html : null;
    }

    // ---- built-in variables ----

    [Theory]
    [InlineData(9, "Good morning")]
    [InlineData(11, "Good morning")]
    [InlineData(12, "Good afternoon")]
    [InlineData(16, "Good afternoon")]
    [InlineData(17, "Good evening")]
    [InlineData(23, "Good evening")]
    public void Greeting_VariesByBand(int hour, string expected)
    {
        var now = new DateTimeOffset(2026, 1, 15, hour, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, TemplateEngine.Resolve("{{greeting}}", None, now));
    }

    [Fact]
    public void Date_UsesShortDate()
    {
        var expected = Morning.ToString("d", CultureInfo.CurrentCulture);
        Assert.Equal(expected, TemplateEngine.Resolve("{{date}}", None, Morning));
    }

    [Fact]
    public void Time_UsesShortTime()
    {
        var expected = Morning.ToString("t", CultureInfo.CurrentCulture);
        Assert.Equal(expected, TemplateEngine.Resolve("{{time}}", None, Morning));
    }

    [Fact]
    public void Day_UsesDayName()
    {
        var expected = Morning.ToString("dddd", CultureInfo.CurrentCulture);
        Assert.Equal(expected, TemplateEngine.Resolve("{{day}}", None, Morning));
        Assert.Equal("Thursday", expected); // en machine sanity
    }

    [Fact]
    public void Date_WithCustomFormat()
    {
        Assert.Equal("2026-01-15", TemplateEngine.Resolve("{{date:yyyy-MM-dd}}", None, Morning));
    }

    [Fact]
    public void Date_WithInvalidFormat_YieldsMarkerNotThrow()
    {
        // A lone '%' is an invalid custom format specifier in .NET.
        var result = TemplateEngine.Resolve("{{date:%}}", None, Morning);
        Assert.Contains("invalid format", result);
    }

    [Fact]
    public void Builtins_AreCaseInsensitive_AndTrimWhitespace()
    {
        Assert.Equal("Good afternoon", TemplateEngine.Resolve("{{  GREETING  }}", None, Afternoon));
        Assert.Equal("2026-01-15", TemplateEngine.Resolve("{{ Date:yyyy-MM-dd }}", None, Evening));
    }

    [Fact]
    public void MixedText_ResolvesInline()
    {
        var result = TemplateEngine.Resolve("<p>{{greeting}}, team.</p>", None, Morning);
        Assert.Equal("<p>Good morning, team.</p>", result);
    }

    // ---- includes ----

    [Fact]
    public void Include_Single()
    {
        var lookup = Lookup(new() { ["welcome"] = "<p>Hi there</p>" });
        Assert.Equal("<p>Hi there</p>", TemplateEngine.Resolve("{{welcome}}", lookup, Morning));
    }

    [Fact]
    public void Include_Nested()
    {
        var lookup = Lookup(new()
        {
            ["welcome"] = "Hi {{name}}",
            ["name"] = "Bob",
        });
        Assert.Equal("Hi Bob", TemplateEngine.Resolve("{{welcome}}", lookup, Morning));
    }

    [Fact]
    public void Include_Chain_WelcomeBodyClosing()
    {
        var lookup = Lookup(new()
        {
            ["welcome"] = "Hello",
            ["closing"] = "Bye",
        });
        var result = TemplateEngine.Resolve("{{welcome}}\nMain content\n{{closing}}", lookup, Morning);
        Assert.Equal("Hello\nMain content\nBye", result);
    }

    [Fact]
    public void Include_ResolvesBuiltinsInBody()
    {
        var lookup = Lookup(new() { ["welcome"] = "{{greeting}}!" });
        Assert.Equal("Good morning!", TemplateEngine.Resolve("{{welcome}}", lookup, Morning));
    }

    [Fact]
    public void Include_IsCaseInsensitive()
    {
        var lookup = Lookup(new() { ["welcome"] = "Hi" });
        Assert.Equal("Hi", TemplateEngine.Resolve("{{ WELCOME }}", lookup, Morning));
    }

    [Fact]
    public void Include_Cycle_YieldsMarkerAndTerminates()
    {
        // A → B → A. Must produce a marker and return (this test completing proves no hang).
        var lookup = Lookup(new()
        {
            ["a"] = "{{b}}",
            ["b"] = "{{a}}",
        });

        string? result = null;
        var worker = new System.Threading.Thread(() => result = TemplateEngine.Resolve("{{a}}", lookup, Morning))
        {
            IsBackground = true,
        };
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "TemplateEngine hung on a cycle");
        Assert.Contains("[cycle: a]", result);
    }

    [Fact]
    public void Include_SelfCycle_YieldsMarker()
    {
        var lookup = Lookup(new() { ["loop"] = "x {{loop}} y" });
        var result = TemplateEngine.Resolve("{{loop}}", lookup, Morning);
        Assert.Contains("[cycle: loop]", result);
    }

    [Fact]
    public void Include_Missing_YieldsMarker()
    {
        Assert.Equal("[missing: nope]", TemplateEngine.Resolve("{{nope}}", None, Morning));
    }

    // ---- literal braces / edge cases ----

    [Fact]
    public void LiteralBraces_Escape()
    {
        Assert.Equal("{{", TemplateEngine.Resolve("{{{{", None, Morning));
        // {{{{name}} → literal "{{" then literal "name}}"
        Assert.Equal("{{name}}", TemplateEngine.Resolve("{{{{name}}", None, Morning));
        // Escaped token is NOT resolved as a variable.
        Assert.Equal("{{greeting}}", TemplateEngine.Resolve("{{{{greeting}}", None, Morning));
    }

    [Fact]
    public void NoTokens_TextUnchanged()
    {
        const string html = "<p>Hello <b>world</b>, nothing to resolve.</p>";
        Assert.Equal(html, TemplateEngine.Resolve(html, None, Morning));
    }

    [Fact]
    public void UnterminatedToken_LeftLiteral()
    {
        Assert.Equal("start {{oops", TemplateEngine.Resolve("start {{oops", None, Morning));
    }

    [Fact]
    public void NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", TemplateEngine.Resolve("", None, Morning));
        Assert.Equal("", TemplateEngine.Resolve(null!, None, Morning));
    }

    // ---- ContainsTokens ----

    [Fact]
    public void ContainsTokens_True()
    {
        Assert.True(TemplateEngine.ContainsTokens("<p>{{date}}</p>"));
        Assert.True(TemplateEngine.ContainsTokens("{{welcome}}"));
    }

    [Fact]
    public void ContainsTokens_False()
    {
        Assert.False(TemplateEngine.ContainsTokens("<p>no tokens here</p>"));
        Assert.False(TemplateEngine.ContainsTokens(""));
        Assert.False(TemplateEngine.ContainsTokens(null));
        Assert.False(TemplateEngine.ContainsTokens("{{{{")); // escape only, no real token
    }
}
