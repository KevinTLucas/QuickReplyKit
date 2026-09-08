using System.IO;
using System.Xml.Linq;

namespace QuickReplyKit.Tests;

public class AboutWindowChromeTests
{
    [Fact]
    public void UsesNativeCloseAndEscapeWithoutInContentCloseButton()
    {
        string xamlPath = TestFiles.RepoAsset("src/QuickReplyKit/Views/AboutWindow.xaml");
        var window = XDocument.Load(xamlPath).Root!;

        Assert.DoesNotContain(window.Descendants(), element =>
            element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == "Close");
        Assert.Null(window.Attribute("WindowStyle"));
        Assert.Equal("CanResizeWithGrip", (string?)window.Attribute("ResizeMode"));
        Assert.Equal("Window_PreviewKeyDown", (string?)window.Attribute("PreviewKeyDown"));

        string code = File.ReadAllText(Path.ChangeExtension(xamlPath, ".xaml.cs"));
        Assert.Contains("e.Key != Key.Escape", code, StringComparison.Ordinal);
        Assert.Contains("Close();", code, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", code, StringComparison.Ordinal);
    }
}
