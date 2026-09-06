using System.IO;
using QuickReplyKit.Core.Library;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Tests;

public class ResponseFileTests
{
    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        using var t = new TempDir();
        var item = new ResponseItem
        {
            Name = "Greeting",
            CategoryPath = "General",
            App = "Outlook",
            Tags = new[] { "hello", "welcome" },
            Html = "<p>Hi &amp; welcome</p>",
            Favorite = true,
            Created = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        };

        ResponseFile.Save(item, t.Path);
        var loaded = ResponseFile.Load(item.FullPath, t.Path);

        Assert.True(loaded.IsValid);
        Assert.Null(loaded.Error);
        Assert.Equal("Greeting", loaded.Name);
        Assert.Equal("General", loaded.CategoryPath);
        Assert.Equal("General/Greeting.json", loaded.RelativePath);
        Assert.Equal("Outlook", loaded.App);
        Assert.Equal(new[] { "hello", "welcome" }, loaded.Tags);
        Assert.Equal("<p>Hi &amp; welcome</p>", loaded.Html);
        Assert.True(loaded.Favorite);
        Assert.Equal(item.Created, loaded.Created);
        Assert.Equal(item.Modified, loaded.Modified);
        Assert.Equal(Path.Combine(t.Path, "General", "Greeting.json"), loaded.FullPath);
    }

    [Fact]
    public void Save_AtRoot_HasEmptyCategoryAndFlatRelativePath()
    {
        using var t = new TempDir();
        var item = new ResponseItem { Name = "Root Item", Html = "<p>hi</p>" };
        ResponseFile.Save(item, t.Path);
        var loaded = ResponseFile.Load(item.FullPath, t.Path);
        Assert.Equal("", loaded.CategoryPath);
        Assert.Equal("Root Item.json", loaded.RelativePath);
    }

    [Fact]
    public void Load_MalformedJson_IsInvalid_DoesNotThrow()
    {
        using var t = new TempDir();
        string p = t.Of("Bad.json");
        File.WriteAllText(p, "{ this is not valid json ");

        var item = ResponseFile.Load(p, t.Path);

        Assert.False(item.IsValid);
        Assert.NotNull(item.Error);
        Assert.Equal("Bad", item.Name); // name still from filename
    }

    [Theory]
    [InlineData("{\"html\":\"<p>x</p>\"}")]      // missing schema
    [InlineData("{\"schema\":1}")]               // missing html
    [InlineData("null")]                          // valid json, not an object
    [InlineData("[1,2,3]")]                       // valid json, wrong shape
    public void Load_NotOurSchema_IsInvalid(string json)
    {
        using var t = new TempDir();
        string p = t.Of("Item.json");
        File.WriteAllText(p, json);

        var item = ResponseFile.Load(p, t.Path);

        Assert.False(item.IsValid);
        Assert.NotNull(item.Error);
    }

    [Fact]
    public void Load_MissingTimestamps_LeavesThemDefault_StillValid()
    {
        // Created/Modified are never displayed or used for sort/filter, so when absent we don't pay a
        // per-file syscall to synthesize them (they stay default). The response is still valid, and
        // Save() will fill Created on the next write.
        using var t = new TempDir();
        string p = t.Of("NoTimes.json");
        File.WriteAllText(p, "{\"schema\":1,\"html\":\"<p>x</p>\"}");

        var item = ResponseFile.Load(p, t.Path);

        Assert.True(item.IsValid);
        Assert.Equal(default, item.Created);
        Assert.Equal(default, item.Modified);
    }

    // ---- Lenient per-field parsing (Phase R) ----

    [Fact]
    public void Load_TagsAsSingleString_BecomesOneTag()
    {
        using var t = new TempDir();
        string p = t.Of("StrTags.json");
        File.WriteAllText(p, "{\"schema\":1,\"html\":\"<p>x</p>\",\"tags\":\"solo\"}");

        var item = ResponseFile.Load(p, t.Path);

        Assert.True(item.IsValid, item.Error);
        Assert.Equal(new[] { "solo" }, item.Tags);
    }

    [Fact]
    public void Load_TagsWithWrongTypedElements_SkipsThemKeepsStrings()
    {
        using var t = new TempDir();
        string p = t.Of("MixedTags.json");
        File.WriteAllText(p, "{\"schema\":1,\"html\":\"<p>x</p>\",\"tags\":[\"a\",5,null,{\"x\":1},\"b\"]}");

        var item = ResponseFile.Load(p, t.Path);

        Assert.True(item.IsValid, item.Error);
        Assert.Equal(new[] { "a", "b" }, item.Tags);
    }

    [Fact]
    public void Load_TagsAsWrongContainer_YieldsEmpty()
    {
        using var t = new TempDir();
        string p = t.Of("ObjTags.json");
        File.WriteAllText(p, "{\"schema\":1,\"html\":\"<p>x</p>\",\"tags\":{\"nope\":true}}");

        var item = ResponseFile.Load(p, t.Path);

        Assert.True(item.IsValid, item.Error);
        Assert.Empty(item.Tags);
    }

    [Theory]
    [InlineData("\"true\"")]  // favorite as a string, not a bool
    [InlineData("\"nonsense\"")]
    [InlineData("1")]          // favorite as a number
    public void Load_FavoriteWrongType_FallsBackToFalse(string favoriteJson)
    {
        using var t = new TempDir();
        string p = t.Of("Fav.json");
        File.WriteAllText(p, $"{{\"schema\":1,\"html\":\"<p>x</p>\",\"favorite\":{favoriteJson}}}");

        var item = ResponseFile.Load(p, t.Path);

        Assert.True(item.IsValid, item.Error);
        Assert.False(item.Favorite);
    }

    [Fact]
    public void Load_AppWrongType_FallsBackToEmpty()
    {
        using var t = new TempDir();
        string p = t.Of("App.json");
        File.WriteAllText(p, "{\"schema\":1,\"html\":\"<p>x</p>\",\"app\":123}");

        var item = ResponseFile.Load(p, t.Path);

        Assert.True(item.IsValid, item.Error);
        Assert.Equal("", item.App);
    }

    [Fact]
    public void Load_UnparseableDates_LeaveThemDefault_StillValid()
    {
        using var t = new TempDir();
        string p = t.Of("BadDates.json");
        File.WriteAllText(p, "{\"schema\":1,\"html\":\"<p>x</p>\",\"created\":\"not-a-date\",\"modified\":42}");

        var item = ResponseFile.Load(p, t.Path);

        // An unparseable date is treated like an absent one: default, not a filesystem time.
        Assert.True(item.IsValid, item.Error);
        Assert.Equal(default, item.Created);
        Assert.Equal(default, item.Modified);
    }

    [Fact]
    public void Load_TrailingCommaAndComments_StillParses()
    {
        using var t = new TempDir();
        string p = t.Of("Lenient.json");
        File.WriteAllText(p,
            "{\n" +
            "  // a hand-added note\n" +
            "  \"schema\": 1,\n" +
            "  \"html\": \"<p>hi</p>\",\n" +
            "  \"tags\": [\"x\", \"y\",],\n" +   // trailing comma inside array
            "}\n");                                // trailing comma inside object

        var item = ResponseFile.Load(p, t.Path);

        Assert.True(item.IsValid, item.Error);
        Assert.Equal("<p>hi</p>", item.Html);
        Assert.Equal(new[] { "x", "y" }, item.Tags);
    }

    [Fact]
    public void Load_OneWrongTypedField_KeepsResponseValid_WithGoodHtml()
    {
        using var t = new TempDir();
        string p = t.Of("OneTypo.json");
        // Everything but html is the wrong type; html is good → the response survives.
        File.WriteAllText(p,
            "{\"schema\":1,\"html\":\"<p>keep me</p>\",\"app\":true,\"tags\":\"onetag\"," +
            "\"favorite\":\"yes\",\"created\":\"garbage\",\"modified\":[]}");

        var item = ResponseFile.Load(p, t.Path);

        Assert.True(item.IsValid, item.Error);
        Assert.Equal("<p>keep me</p>", item.Html);
        Assert.Equal("", item.App);
        Assert.Equal(new[] { "onetag" }, item.Tags);
        Assert.False(item.Favorite);
    }

    [Fact]
    public void Load_NoUsableHtml_IsInvalid_EvenWithSchema()
    {
        using var t = new TempDir();
        string p = t.Of("NoHtml.json");
        File.WriteAllText(p, "{\"schema\":1,\"html\":123}"); // html present but not a string

        var item = ResponseFile.Load(p, t.Path);

        Assert.False(item.IsValid);
        Assert.NotNull(item.Error);
    }

    [Fact]
    public void Scan_BrokenFile_DoesNotBlockValidSibling()
    {
        using var t = new TempDir();
        File.WriteAllText(t.Of("broken.json"), "{ not json at all ");
        File.WriteAllText(t.Of("also-broken.json"), "{\"schema\":1}"); // no html
        TestFiles.WriteResponse(t.Of("good.json"), html: "<p>fine</p>", tags: new[] { "ok" });

        var snap = LibraryScanner.Scan(t.Path);

        var good = Assert.Single(snap.ValidResponses);
        Assert.Equal("good", good.Name);
        Assert.Contains(snap.AllResponses, r => r.Name == "broken" && !r.IsValid);
        Assert.Contains(snap.AllResponses, r => r.Name == "also-broken" && !r.IsValid);
    }
}
