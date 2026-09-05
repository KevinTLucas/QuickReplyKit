using System.IO;
using QuickReplyKit.Core.Library;

namespace QuickReplyKit.Tests;

public class LibrarySeederTests
{
    [Fact]
    public void SeedIfEmpty_CopiesTree_WhenLibraryEmpty()
    {
        using var t = new TempDir();
        string src = t.Of("src");
        TestFiles.WriteResponse(Path.Combine(src, "General", "A.json"));
        TestFiles.WriteResponse(Path.Combine(src, "General", "Sub", "B.json"));
        string dst = t.Of("lib");

        LibrarySeeder.SeedIfEmpty(dst, src);

        Assert.True(File.Exists(Path.Combine(dst, "General", "A.json")));
        Assert.True(File.Exists(Path.Combine(dst, "General", "Sub", "B.json")));
    }

    [Fact]
    public void SeedIfEmpty_NoOp_WhenLibraryAlreadyHasResponses()
    {
        using var t = new TempDir();
        string src = t.Of("src");
        TestFiles.WriteResponse(Path.Combine(src, "FromSource.json"));
        string dst = t.Of("lib");
        TestFiles.WriteResponse(Path.Combine(dst, "Existing.json"));

        LibrarySeeder.SeedIfEmpty(dst, src);

        Assert.False(File.Exists(Path.Combine(dst, "FromSource.json")));
        Assert.True(File.Exists(Path.Combine(dst, "Existing.json")));
    }

    [Fact]
    public void SeedIfEmpty_MissingSource_IsNoOp()
    {
        using var t = new TempDir();
        string dst = t.Of("lib");
        LibrarySeeder.SeedIfEmpty(dst, t.Of("missing-source"));
        Assert.False(Directory.Exists(dst));
    }
}
