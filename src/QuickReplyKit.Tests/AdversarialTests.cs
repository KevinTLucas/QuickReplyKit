using System.IO;
using System.Text;
using System.Text.Json;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Library;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Search;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Core.Text;

namespace QuickReplyKit.Tests;

// ---------------------------------------------------------------------------
// Adversarial edge-case tests added by the Phase 1 QA pass. These probe
// malformed input, path handling, search corner cases, and text conversion.
// ---------------------------------------------------------------------------

public class ParserAdversarialTests
{
    private static ResponseItem Load(TempDir t, string content, string name = "Item.json",
        Encoding? enc = null)
    {
        string p = t.Of(name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content, enc ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return ResponseFile.Load(p, t.Path);
    }

    [Fact]
    public void EmptyFile_IsInvalid_NoThrow()
    {
        using var t = new TempDir();
        var item = Load(t, "");
        Assert.False(item.IsValid);
        Assert.NotNull(item.Error);
        Assert.Equal("Item", item.Name);
    }

    [Fact]
    public void EmptyObject_IsInvalid()
    {
        using var t = new TempDir();
        Assert.False(Load(t, "{}").IsValid);
    }

    [Fact]
    public void NullHtml_IsInvalid()
    {
        using var t = new TempDir();
        var item = Load(t, "{\"schema\":1,\"html\":null}");
        Assert.False(item.IsValid);
    }

    [Fact]
    public void ExtraUnknownFields_AreIgnored_StillValid()
    {
        using var t = new TempDir();
        var item = Load(t, "{\"schema\":1,\"html\":\"<p>hi</p>\",\"mystery\":42,\"nested\":{\"a\":1},\"future\":\"x\"}");
        Assert.True(item.IsValid, item.Error);
        Assert.Equal("<p>hi</p>", item.Html);
    }

    [Fact]
    public void Utf8Bom_IsToleratedByReader()
    {
        using var t = new TempDir();
        // UTF-8 BOM prefix; File.ReadAllText should strip it so parsing succeeds.
        var item = Load(t, "{\"schema\":1,\"html\":\"<p>b</p>\"}", enc: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Assert.True(item.IsValid, item.Error);
        Assert.Equal("<p>b</p>", item.Html);
    }

    [Fact]
    public void HugeHtml_RoundTrips()
    {
        using var t = new TempDir();
        string big = "<p>" + new string('x', 500_000) + "</p>";
        var dto = new { schema = 1, html = big };
        var item = Load(t, JsonSerializer.Serialize(dto));
        Assert.True(item.IsValid, item.Error);
        Assert.Equal(big.Length, item.Html.Length);
    }

    // ---- Lenient per-field parsing (Phase R): a single wrong-typed/odd field is recovered
    //      to a default and never discards a response whose html is usable. ----

    [Fact]
    public void TagsAsString_NotArray_BecomesOneTag()
    {
        using var t = new TempDir();
        // A bare string is read as a single tag rather than invalidating the whole file.
        var item = Load(t, "{\"schema\":1,\"html\":\"<p>x</p>\",\"tags\":\"licensing\"}");
        Assert.True(item.IsValid, item.Error);
        Assert.Equal(new[] { "licensing" }, item.Tags);
    }

    [Fact]
    public void TrailingCommas_AreTolerated()
    {
        using var t = new TempDir();
        // Hand-edited trailing commas are accepted (AllowTrailingCommas).
        var item = Load(t, "{\"schema\":1,\"html\":\"<p>x</p>\",}");
        Assert.True(item.IsValid, item.Error);
    }

    [Fact]
    public void SchemaAsString_StillMarksTheFileAsOurs()
    {
        using var t = new TempDir();
        // schema is a presence marker; a wrong-typed schema does not discard good html.
        var item = Load(t, "{\"schema\":\"1\",\"html\":\"<p>x</p>\"}");
        Assert.True(item.IsValid, item.Error);
    }

    [Fact]
    public void ValidJsonWrongSchema_IsInvalid_ButMarked()
    {
        using var t = new TempDir();
        var item = Load(t, "{\"foo\":\"bar\",\"baz\":123}");
        Assert.False(item.IsValid);
        Assert.NotNull(item.Error);
        Assert.Equal("Item", item.Name); // name still recovered from filename
    }
}

public class PathAdversarialTests
{
    [Fact]
    public void UnicodeAndSpaces_InNames_RoundTrip()
    {
        using var t = new TempDir();
        string name = "Málaga café 日本語 (v2)";
        TestFiles.WriteResponse(t.Of("Ünïcode Földer", name + ".json"));

        var snap = LibraryScanner.Scan(t.Path);
        var item = Assert.Single(snap.AllResponses);
        Assert.Equal(name, item.Name);
        Assert.Equal("Ünïcode Földer", item.CategoryPath);
        Assert.Equal("Ünïcode Földer/" + name + ".json", item.RelativePath);
    }

    [Fact]
    public void VeryDeepNesting_IsWalked()
    {
        using var t = new TempDir();
        var parts = new List<string>();
        for (int i = 0; i < 15; i++) parts.Add($"L{i:D2}");
        parts.Add("Deep.json");
        TestFiles.WriteResponse(t.Of(parts.ToArray()));

        var snap = LibraryScanner.Scan(t.Path);
        var item = Assert.Single(snap.AllResponses);
        Assert.True(item.IsValid, item.Error);
        Assert.Equal("Deep", item.Name);
        Assert.StartsWith("L00/L01/L02", item.CategoryPath);
        Assert.EndsWith("L14", item.CategoryPath);
    }

    [Fact]
    public void NonJsonFiles_AreIgnored()
    {
        using var t = new TempDir();
        TestFiles.WriteResponse(t.Of("Real.json"));
        File.WriteAllText(t.Of("notes.txt"), "not a response");
        File.WriteAllText(t.Of("data.json5"), "{\"schema\":1,\"html\":\"<p>x</p>\"}");
        File.WriteAllText(t.Of("config.jsonc"), "{\"schema\":1,\"html\":\"<p>x</p>\"}");
        File.WriteAllText(t.Of("readme"), "no extension");

        var snap = LibraryScanner.Scan(t.Path);

        // Only the true .json file is enumerated; .json5/.jsonc/.txt/extensionless ignored.
        var item = Assert.Single(snap.AllResponses);
        Assert.Equal("Real", item.Name);
    }

    [Fact]
    public void DuplicateNames_InDifferentFolders_AreDistinct()
    {
        using var t = new TempDir();
        TestFiles.WriteResponse(t.Of("A", "Same.json"));
        TestFiles.WriteResponse(t.Of("B", "Same.json"));

        var snap = LibraryScanner.Scan(t.Path);

        Assert.Equal(2, snap.AllResponses.Count);
        Assert.Equal(2, snap.AllResponses.Select(r => r.RelativePath).Distinct().Count());
        Assert.All(snap.AllResponses, r => Assert.Equal("Same", r.Name));
    }

    [Fact]
    public void EmptyLibraryFolder_YieldsEmptySnapshot()
    {
        using var t = new TempDir();
        var snap = LibraryScanner.Scan(t.Path);
        Assert.Empty(snap.AllResponses);
        Assert.Empty(snap.Root.Children);
        Assert.Empty(snap.Root.Responses);
    }

    [Fact]
    public void ValidJsonWrongSchema_InScan_MarkedInvalid_DoesNotBlockSiblings()
    {
        using var t = new TempDir();
        TestFiles.WriteResponse(t.Of("Good.json"));
        File.WriteAllText(t.Of("Stranger.json"), "{\"totally\":\"different\",\"shape\":[1,2,3]}");

        var snap = LibraryScanner.Scan(t.Path);

        Assert.Equal(2, snap.AllResponses.Count);
        Assert.Single(snap.ValidResponses);
        Assert.Contains(snap.AllResponses, r => r.Name == "Stranger" && !r.IsValid);
    }

    [Fact]
    public void RelativePaths_UseForwardSlashes_OnWindows()
    {
        using var t = new TempDir();
        TestFiles.WriteResponse(t.Of("Cat", "Sub", "R.json"));
        var snap = LibraryScanner.Scan(t.Path);
        var item = Assert.Single(snap.AllResponses);
        Assert.DoesNotContain('\\', item.RelativePath);
        Assert.DoesNotContain('\\', item.CategoryPath);
        Assert.Equal("Cat/Sub/R.json", item.RelativePath);
    }
}

public class SearchAdversarialTests
{
    private static ResponseItem Item(string name, string app = "", string[]? tags = null,
        string html = "<p></p>", string category = "") =>
        new() { Name = name, App = app, Tags = tags ?? Array.Empty<string>(), Html = html, CategoryPath = category, IsValid = true };

    [Theory]
    [InlineData(".")]
    [InlineData("*")]
    [InlineData("[")]
    [InlineData("(v2)")]
    [InlineData("a+b")]
    public void RegexSpecialChars_AreLiteral(string special)
    {
        var match = Item("Name " + special + " here");
        var noMatch = Item("Unrelated");
        var results = ResponseSearch.Search(new[] { match, noMatch }, special);
        Assert.Single(results);
        Assert.Equal(match.Name, results[0].Name);
    }

    [Fact]
    public void LeadingTrailingAndMultipleSpaces_AreIgnored()
    {
        var both = Item("Password Reset");
        var results = ResponseSearch.Search(new[] { both }, "   reset    password   ");
        Assert.Single(results);
    }

    [Fact]
    public void QueryLongerThanAnyField_ReturnsNothing()
    {
        var item = Item("Reset", app: "Outlook", tags: new[] { "hi" }, html: "<p>short</p>");
        Assert.Empty(ResponseSearch.Search(new[] { item }, "supercalifragilisticexpialidocious"));
    }

    [Fact]
    public void TokensMatchingDifferentFields_AllMustMatch()
    {
        var item = Item("Welcome Guide", app: "Outlook", tags: new[] { "reset" }, category: "Onboarding");
        // name + app + tag + category tokens all present → match
        Assert.Single(ResponseSearch.Search(new[] { item }, "welcome outlook reset onboarding"));
        // one token (matches nothing) breaks the AND
        Assert.Empty(ResponseSearch.Search(new[] { item }, "welcome nonexistenttoken"));
    }

    [Fact]
    public void Ranking_FollowsSpecFieldOrder()
    {
        // Same token hits different fields on different items; expect name-prefix best.
        var namePrefix = Item("Reset stuff");
        var nameContains = Item("Hard Reset");
        var tagExact = Item("Alpha", tags: new[] { "reset" });
        var tagContains = Item("Beta", tags: new[] { "resetter" });
        var appMatch = Item("Gamma", app: "ResetApp");
        var categoryMatch = Item("Delta", category: "Reset Guides");
        var bodyMatch = Item("Omega", html: "<p>please reset</p>");

        var input = new[] { bodyMatch, categoryMatch, appMatch, tagContains, tagExact, nameContains, namePrefix };
        var results = ResponseSearch.Search(input, "reset");

        Assert.Equal(
            new[] { "Reset stuff", "Hard Reset", "Alpha", "Beta", "Gamma", "Delta", "Omega" },
            results.Select(r => r.Name));
    }

    [Fact]
    public void TiesAreBrokenByName_StableOrdering()
    {
        // All match name-contains at identical score; ties break by name using
        // OrdinalIgnoreCase (per the spec's "then by name"), independent of input order.
        var items = new[] { Item("zebra x"), Item("apple x"), Item("Mango x") };
        var results = ResponseSearch.Search(items, "x");
        Assert.Equal(new[] { "apple x", "Mango x", "zebra x" }, results.Select(r => r.Name));
    }

    [Fact]
    public void InvalidItems_MatchByName_ButRankBelowValid()
    {
        // M4: a broken file must be findable by name so it can be fixed — but it ranks after
        // every valid hit and stays non-pasteable in the UI.
        var invalid = new ResponseItem { Name = "Reset", IsValid = false };
        var valid = Item("Reset Guide");

        var results = ResponseSearch.Search(new[] { invalid, valid }, "reset");

        Assert.Equal(new[] { "Reset Guide", "Reset" }, results.Select(r => r.Name));
    }
}

public class HtmlTextAdversarialTests
{
    [Fact]
    public void UnclosedTag_AtEnd_BecomesLiteralText()
    {
        // No '>' after the last '<' → remainder treated as text (decoded).
        Assert.Equal("Hello <b", HtmlText.ToPlainText("<p>Hello <b"));
    }

    [Fact]
    public void DeeplyNestedFormatting_StripsAll_KeepsText()
    {
        Assert.Equal("abc", HtmlText.ToPlainText("<b><i><u>a<b>b</b>c</u></i></b>"));
    }

    [Fact]
    public void NestedUnorderedLists_BothBulleted()
    {
        var html = "<ul><li>a<ul><li>b</li></ul></li><li>c</li></ul>";
        Assert.Equal("• a\n• b\n• c", HtmlText.ToPlainText(html));
    }

    [Fact]
    public void NestedOrderedLists_CounterPerLevel()
    {
        var html = "<ol><li>one<ol><li>inner</li></ol></li><li>two</li></ol>";
        // Inner list is its own counter starting at 1; outer resumes at 2.
        Assert.Equal("1. one\n1. inner\n2. two", HtmlText.ToPlainText(html));
    }

    [Fact]
    public void NumericAndHexEntities_AreDecoded()
    {
        Assert.Equal("A→B©—",
            HtmlText.ToPlainText("<p>A&#8594;B&#xA9;&mdash;</p>"));
    }

    [Fact]
    public void Hyperlink_KeepsText_DropsUrl()
    {
        var plain = HtmlText.ToPlainText("<p><a href=\"https://example.com/reset\">Click</a></p>");
        Assert.Equal("Click", plain);
        Assert.DoesNotContain("example.com", plain);
    }

    [Theory]
    [InlineData("<p>a<br>b</p>", "a\nb")]
    [InlineData("<p>a<br/>b</p>", "a\nb")]
    [InlineData("<p>a<br />b</p>", "a\nb")]
    [InlineData("<p>a<BR>b</p>", "a\nb")]
    public void BrVariants_AllBecomeNewline(string html, string expected)
    {
        Assert.Equal(expected, HtmlText.ToPlainText(html));
    }
}

public class CfHtmlAdversarialTests
{
    private static int Offset(string cf, string key)
    {
        int i = cf.IndexOf(key, StringComparison.Ordinal) + key.Length;
        int j = i;
        while (j < cf.Length && char.IsDigit(cf[j])) j++;
        return int.Parse(cf[i..j]);
    }

    private static string ExtractFragment(string cf)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(cf);
        int s = Offset(cf, "StartFragment:");
        int e = Offset(cf, "EndFragment:");
        return Encoding.UTF8.GetString(bytes, s, e - s);
    }

    [Fact]
    public void FragmentContainingMarkerText_OffsetsStillDelimitExactly()
    {
        // The fragment itself contains the literal marker comments. Offsets are positional
        // (byte counts), not the result of searching for the markers, so they must still
        // delimit exactly the intended fragment including the embedded marker text.
        string fragment = "<p>before <!--StartFragment--> mid <!--EndFragment--> after</p>";
        string cf = CfHtml.Build(fragment);
        Assert.Equal(fragment, ExtractFragment(cf));
    }

    [Fact]
    public void FragmentWithMarkerText_AndMultibyte_OffsetsExact()
    {
        string fragment = "<p>café <!--EndFragment--> 日本語 😀 <!--StartFragment--></p>";
        string cf = CfHtml.Build(fragment);
        Assert.Equal(fragment, ExtractFragment(cf));
    }

    [Fact]
    public void EmptyFragment_StartEqualsEnd()
    {
        string cf = CfHtml.Build("");
        Assert.Equal(Offset(cf, "StartFragment:"), Offset(cf, "EndFragment:"));
        Assert.Equal("", ExtractFragment(cf));
    }

    [Fact]
    public void NullFragment_TreatedAsEmpty()
    {
        string cf = CfHtml.Build(null!);
        Assert.Equal("", ExtractFragment(cf));
        Assert.Equal(Encoding.UTF8.GetByteCount(cf), Offset(cf, "EndHTML:"));
    }

    [Fact]
    public void EndHtml_AlwaysEqualsTotalByteLength_ForMultibyte()
    {
        string cf = CfHtml.Build("<p>😀🎉 café 日本語</p>");
        Assert.Equal(Encoding.UTF8.GetByteCount(cf), Offset(cf, "EndHTML:"));
        // StartHTML points exactly at "<html>".
        byte[] bytes = Encoding.UTF8.GetBytes(cf);
        Assert.Equal("<html>", Encoding.UTF8.GetString(bytes, Offset(cf, "StartHTML:"), 6));
    }
}

public class RecentStoreAdversarialTests
{
    [Fact]
    public void Dedup_IsCaseInsensitive()
    {
        var store = new RecentStore(cap: 5);
        store.Add("General/Ticket.json");
        store.Add("general/ticket.JSON"); // same path, different case → dedup, move to front
        Assert.Single(store.Items);
        Assert.Equal("general/ticket.JSON", store.Items[0].RelativePath);
    }

    [Fact]
    public void Load_FileWithMoreThanCap_IsTrimmed()
    {
        using var t = new TempDir();
        string path = t.Of("recent.json");
        var many = Enumerable.Range(0, 100)
            .Select(i => new RecentEntry { RelativePath = $"r{i}.json", UsedAt = DateTimeOffset.UtcNow });
        File.WriteAllText(path, JsonSerializer.Serialize(many));

        var store = RecentStore.Load(path, cap: 10);
        Assert.Equal(10, store.Items.Count);
    }

    [Fact]
    public void Load_SkipsEntriesWithEmptyPath()
    {
        using var t = new TempDir();
        string path = t.Of("recent.json");
        File.WriteAllText(path, "[{\"relativePath\":\"\"},{\"relativePath\":\"ok.json\"}]");
        var store = RecentStore.Load(path);
        Assert.Single(store.Items);
        Assert.Equal("ok.json", store.Items[0].RelativePath);
    }
}

public class AppConfigAdversarialTests
{
    [Fact]
    public void Load_ExplicitNulls_FallBackToDefaults()
    {
        using var t = new TempDir();
        string path = t.Of("config.json");
        File.WriteAllText(path, "{\"hotkeyGesture\":null,\"libraryPath\":null,\"window\":null}");

        var cfg = AppConfig.Load(path);
        Assert.Equal("Ctrl+Alt+D", cfg.HotkeyGesture);
        Assert.Equal(AppPaths.DefaultLibraryDir, cfg.LibraryPath);
        Assert.NotNull(cfg.Window);
    }

    [Fact]
    public void Load_EmptyObject_UsesDefaults()
    {
        using var t = new TempDir();
        string path = t.Of("config.json");
        File.WriteAllText(path, "{}");
        var cfg = AppConfig.Load(path);
        Assert.Equal("Ctrl+Alt+D", cfg.HotkeyGesture);
        Assert.Equal(PasteMode.RichText, cfg.DefaultPasteMode);
    }
}
