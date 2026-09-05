using System.IO;
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
    public void Load_MissingTimestamps_FallsBackToFileTimes()
    {
        using var t = new TempDir();
        string p = t.Of("NoTimes.json");
        File.WriteAllText(p, "{\"schema\":1,\"html\":\"<p>x</p>\"}");

        var item = ResponseFile.Load(p, t.Path);

        Assert.True(item.IsValid);
        Assert.NotEqual(default, item.Created);
        Assert.NotEqual(default, item.Modified);
    }
}
