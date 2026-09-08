using System.Xml.Linq;

namespace QuickReplyKit.Tests;

public class LauncherResponseActionsTests
{
    [Fact]
    public void TransientComposeAction_HasClearLabelAndExistingHandler()
    {
        var document = XDocument.Load(TestFiles.RepoAsset("src/QuickReplyKit/Views/LauncherWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var action = document.Descendants()
            .Single(element => (string?)element.Attribute(x + "Name") == "MenuEditInsert");

        Assert.Equal("Edit Response Text…", (string?)action.Attribute("Header"));
        Assert.Equal("MenuEditInsert_Click", (string?)action.Attribute("Click"));
    }
}
