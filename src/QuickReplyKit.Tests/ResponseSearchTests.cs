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
    public void Search_MatchesInvalidByName_RankedBelowValid()
    {
        // M4: invalid files are now findable by NAME (so they can be fixed), but rank after every
        // valid match. The UI keeps them non-pasteable (covered in LauncherViewModelTests).
        var valid = Item("Password Reset");
        var invalid = Item("Password Broken", valid: false);

        var results = ResponseSearch.Search(new[] { invalid, valid }, "password");

        Assert.Equal(new[] { "Password Reset", "Password Broken" }, results.Select(r => r.Name));
    }

    [Fact]
    public void Search_DoesNotMatchInvalidByBodyOrTag()
    {
        // Invalid files match by name only — a body/tag hit must not surface them.
        var invalid = Item("Unrelated", tags: new[] { "password" },
            html: "<p>reset your password</p>", valid: false);

        Assert.Empty(ResponseSearch.Search(new[] { invalid }, "password"));
    }

    [Fact]
    public void AppAndCategory_AreSearchable()
    {
        var byApp = Item("Reset Guide", app: "Outlook");
        var byCategory = Item("Reset Guide 2", category: "Clever Apps/IXL");

        Assert.Single(ResponseSearch.Search(new[] { byApp }, "outlook"));
        Assert.Single(ResponseSearch.Search(new[] { byCategory }, "ixl"));
    }

    // --- M2: in-scope ranking ----------------------------------------------

    [Fact]
    public void Scope_RanksInScopeHitsFirst_KeepingGlobalResults()
    {
        var inScope = Item("License Reset", category: "Billing");
        var inScopeDeep = Item("License Renewal", category: "Billing/Sub");
        var outOfScope = Item("License Info", category: "General");

        // Same field match for all; only the selected scope should reorder them.
        var results = ResponseSearch.Search(
            new[] { outOfScope, inScope, inScopeDeep }, "license", scope: "Billing");

        Assert.Equal(3, results.Count);                 // global search still returns everything
        Assert.Equal("General", results[2].CategoryPath); // out-of-scope sinks to the bottom
        Assert.All(results.Take(2), r => Assert.StartsWith("Billing", r.CategoryPath));
    }

    [Fact]
    public void Scope_Empty_LeavesTierOrderingUntouched()
    {
        var prefix = Item("Reset Guide", category: "General");
        var body = Item("Ticket", category: "Billing", html: "<p>please reset</p>");

        var results = ResponseSearch.Search(new[] { body, prefix }, "reset", scope: "");

        Assert.Equal(new[] { "Reset Guide", "Ticket" }, results.Select(r => r.Name));
    }

    // --- M1: lazy fuzzy fallback -------------------------------------------

    [Fact]
    public void Fuzzy_FindsTypo_WhenNoExactMatch()
    {
        var license = Item("License Expired", tags: new[] { "licensing" });
        var results = ResponseSearch.Rank(new[] { license }, "licence");   // British spelling / typo

        Assert.Single(results);
        Assert.Empty(results[0].NameSpans);   // fuzzy hit → nothing highlighted literally
    }

    [Fact]
    public void Fuzzy_DoesNotFire_WhenExactMatchExists()
    {
        // "license" matches exactly; a fuzzy sibling ("licenses" typo'd elsewhere) must not sneak in
        // via the fallback because the token already has an exact match.
        var exact = Item("License Expired");
        var onlyFuzzy = Item("Licence Note");   // would match "license" only fuzzily

        var results = ResponseSearch.Search(new[] { exact, onlyFuzzy }, "license");

        Assert.Single(results);
        Assert.Equal("License Expired", results[0].Name);
    }

    [Fact]
    public void Fuzzy_MatchesBySubsequence_Abbreviation()
    {
        var pwd = Item("Password Reset");
        // "pwd" is not a substring but is a subsequence of "password"; no exact hit → fuzzy fires.
        Assert.Single(ResponseSearch.Search(new[] { pwd }, "pwd"));
    }

    [Fact]
    public void Fuzzy_PerToken_MixesExactAndFallback()
    {
        // "reset" is exact; "licence" has no exact hit anywhere → only that token falls back.
        var match = Item("License Reset");
        var noReset = Item("License Renewal");

        var results = ResponseSearch.Search(new[] { match, noReset }, "licence reset");

        Assert.Single(results);
        Assert.Equal("License Reset", results[0].Name);
    }

    // --- H2: match spans & body snippet ------------------------------------

    [Fact]
    public void NameSpans_CoverEachOccurrence_OfTheToken()
    {
        var hit = ResponseSearch.Rank(new[] { Item("Reset the reset") }, "reset")[0];

        Assert.Equal(2, hit.NameSpans.Count);
        Assert.Equal(0, hit.NameSpans[0].Start);
        Assert.Equal(5, hit.NameSpans[0].Length);
        Assert.Equal(10, hit.NameSpans[1].Start);   // "Reset the [reset]"
        Assert.Null(hit.Snippet);                    // name hit → no snippet
    }

    [Fact]
    public void BodyOnlyMatch_YieldsSnippet_AndNoNameSpans()
    {
        var hit = ResponseSearch.Rank(
            new[] { Item("Onboarding", html: "<p>Remember to reset the account before day one.</p>") },
            "reset")[0];

        Assert.Empty(hit.NameSpans);                       // nothing to highlight in the name
        Assert.NotNull(hit.Snippet);
        Assert.Contains("reset", hit.Snippet!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\n', hit.Snippet!);         // single line
    }

    [Fact]
    public void TagMatch_YieldsNoSnippet()
    {
        // A tag/app/category hit (not body) is already explained by the subtitle — no snippet.
        var hit = ResponseSearch.Rank(new[] { Item("Guide", tags: new[] { "licensing" }) }, "licensing")[0];

        Assert.Empty(hit.NameSpans);
        Assert.Null(hit.Snippet);
    }
}
