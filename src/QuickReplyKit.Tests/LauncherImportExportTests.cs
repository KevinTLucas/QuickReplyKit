using System.Xml.Linq;

namespace QuickReplyKit.Tests;

public class LauncherImportExportTests
{
    [Fact]
    public void Launcher_UsesExplicitImportExportEntryPoint_AndScopedExports()
    {
        var document = XDocument.Load(TestFiles.RepoAsset("src/QuickReplyKit/Views/LauncherWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var button = document.Descendants()
            .Single(element => (string?)element.Attribute(x + "Name") == "ImportExportButton");

        Assert.Equal("Import and export responses", (string?)button.Attribute("AutomationProperties.Name"));
        Assert.Equal("Import responses or export the full library", (string?)button.Attribute("ToolTip"));
        Assert.Contains(button.Descendants(), element =>
            element.Name.LocalName == "AccessText" && (string?)element.Attribute("Text") == "_Import / Export");

        var headers = button.Descendants()
            .Where(element => element.Name.LocalName == "MenuItem")
            .Select(element => (string?)element.Attribute("Header") ?? "")
            .ToArray();
        Assert.Equal(new[] { "_Import responses…", "Export _full library…" }, headers);

        Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("Click") == "MenuExportResponse_Click");
        Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("Click") == "MenuExportFolder_Click");
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "OverflowButton");
    }

    [Fact]
    public void ImportPreview_ReadOnlyCodeSourceBinding_IsOneWay()
    {
        var document = XDocument.Load(TestFiles.RepoAsset("src/QuickReplyKit/Views/ImportPreviewWindow.xaml"));
        var source = document.Descendants().Single(element =>
            element.Name.LocalName == "TextBox"
            && (string?)element.Attribute("Text") == "{Binding CodeSource, Mode=OneWay}");

        Assert.Equal("{Binding CodeSource, Mode=OneWay}", (string?)source.Attribute("Text"));
    }
}
