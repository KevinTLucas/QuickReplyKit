using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Search;

namespace QuickReplyKit.Tests;

public class ResponseSearchTests
{
    private static ResponseItem Item(string name, string app = "", string[]? tags = null,
        string html = "<p></p>", string category = "", bool valid = true) =>
        new()
        {
            Name = name,
            App = app,
            Tags = tags ?? Array.Empty<string>(),
            Html = html,
            CategoryPath = category,
            IsValid = valid,
        };

    [Fact]
    public void NamePrefix_RanksAbove_BodyMatch()
    {
        var byName = Item("Password Reset");
        var byBody = Item("Ticket", html: "<p>Please reset your pass now</p>");

        var results = ResponseSearch.Search(new[] { byBody, byName }, "pass");

        Assert.Equal(2, results.Count);
        Assert.Equal("Password Reset", results[0].Name);
        Assert.Equal("Ticket", results[1].Name);
    }

    [Fact]
    public void MultiToken_RequiresAllTokens()
    {
        var both = Item("Password Reset");                       // matches "reset" + "password"
        var onlyOne = Item("Reset", html: "<p>nothing here</p>"); // missing "password"

        var results = ResponseSearch.Search(new[] { both, onlyOne }, "reset password");

        Assert.Single(results);
        Assert.Equal("Password Reset", results[0].Name);
    }

    [Fact]
    public void TagMatch_IsFound()
    {
        var item = Item("Some Unrelated Name", tags: new[] { "licensing", "campus" });

        var results = ResponseSearch.Search(new[] { item }, "licensing");

        Assert.Single(results);
    }

    [Fact]
    public void EmptyQuery_ReturnsAll_SortedByName()
    {
        var items = new[] { Item("Banana"), Item("apple"), Item("Cherry") };

        var results = ResponseSearch.Search(items, "   ");

        Assert.Equal(new[] { "apple", "Banana", "Cherry" }, results.Select(r => r.Name));
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        var item = Item("Password");
        Assert.Single(ResponseSearch.Search(new[] { item }, "PASSWORD"));
        Assert.Single(ResponseSearch.Search(new[] { item }, "pass"));
    }

    [Fact]
    public void Search_ExcludesInvalidResponses()
    {
        var valid = Item("Password Reset");
        var invalid = Item("Password Broken", valid: false);

        var results = ResponseSearch.Search(new[] { valid, invalid }, "password");

        Assert.Single(results);
        Assert.Equal("Password Reset", results[0].Name);
    }

    [Fact]
    public void AppAndCategory_AreSearchable()
    {
        var byApp = Item("Reset Guide", app: "Outlook");
        var byCategory = Item("Reset Guide 2", category: "Clever Apps/IXL");

        Assert.Single(ResponseSearch.Search(new[] { byApp }, "outlook"));
        Assert.Single(ResponseSearch.Search(new[] { byCategory }, "ixl"));
    }
}
