using System.IO;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Tests;

public class DefaultLibraryTests
{
    [Fact]
    public void SeededFile_NoCampusLicense_ParsesValid()
    {
        string libraryRoot = TestFiles.RepoAsset("assets/DefaultLibrary");
        string file = Path.Combine(libraryRoot, "Clever Apps", "IXL", "Licensing", "No Campus License.json");

        var item = ResponseFile.Load(file, libraryRoot);

        Assert.True(item.IsValid, item.Error);
        Assert.Equal("No Campus License", item.Name);
        Assert.Equal("IXL", item.App);
        Assert.True(item.Favorite);
        Assert.Equal("Clever Apps/IXL/Licensing", item.CategoryPath);
        Assert.Contains("licensing", item.Tags);
        Assert.Contains("campus", item.PlainText, StringComparison.OrdinalIgnoreCase);
    }
}
