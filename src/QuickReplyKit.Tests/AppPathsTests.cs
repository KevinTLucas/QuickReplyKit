using QuickReplyKit.Core;

namespace QuickReplyKit.Tests;

public class AppPathsTests
{
    [Fact]
    public void AppDataDir_EndsWith_AppName()
    {
        Assert.EndsWith("QuickReplyKit", AppPaths.AppDataDir);
    }

    [Fact]
    public void DefaultLibraryDir_IsUnderDocuments()
    {
        Assert.Contains("QuickReplyKit", AppPaths.DefaultLibraryDir);
        Assert.EndsWith("Library", AppPaths.DefaultLibraryDir);
    }
}
