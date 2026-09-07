using System.IO;
using System.Xml.Linq;

namespace QuickReplyKit.Tests;

public class AccessibilityLauncherEditorTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SharedAndLauncherControls_ExposeKeyboardAndAutomationContracts()
    {
        var app = Load("src/QuickReplyKit/App.xaml");
        Assert.DoesNotContain(app.Descendants(), e => (string?)e.Attribute("PopupAnimation") == "Slide");

        var launcher = Load("src/QuickReplyKit/Views/LauncherWindow.xaml");
        foreach (string name in new[] { "SearchBox", "NavTree", "ResultsList", "PreviewViewer" })
            Assert.False(string.IsNullOrWhiteSpace((string?)Named(launcher, name).Attribute("AutomationProperties.Name")));

        Assert.Contains(launcher.Descendants(), e => e.Name.LocalName == "TextBlock" &&
            (string?)e.Attribute("AutomationProperties.LiveSetting") == "Polite" &&
            e.Attributes().Any(a => a.Name.LocalName == "LiveRegion.Content"));
        var favorite = launcher.Descendants().Single(e => e.Name.LocalName == "ToggleButton" &&
            (string?)e.Attribute("Command") == "{Binding ToggleFavoriteCommand}");
        Assert.Equal("{Binding IsFavorite, Mode=OneWay}", (string?)favorite.Attribute("IsChecked"));
    }

    [Fact]
    public void SelectedNav_UsesSystemColorBarAndNonColorTextCue()
    {
        var launcher = Load("src/QuickReplyKit/Views/LauncherWindow.xaml");
        var template = launcher.Descendants().Single(e => e.Name.LocalName == "HierarchicalDataTemplate");
        var trigger = template.Descendants().Single(e => e.Name.LocalName == "DataTrigger" &&
            (string?)e.Attribute("Binding") == "{Binding IsSelected}" && (string?)e.Attribute("Value") == "True");

        Assert.Contains(trigger.Elements(), e => (string?)e.Attribute("TargetName") == "SelectionBar" &&
            (string?)e.Attribute("Property") == "Background" &&
            (string?)e.Attribute("Value") == "{DynamicResource {x:Static SystemColors.HighlightTextBrushKey}}");
        Assert.Contains(trigger.Elements(), e => (string?)e.Attribute("TargetName") == "NavTitle" &&
            (string?)e.Attribute("Property") == "FontWeight" && (string?)e.Attribute("Value") == "SemiBold");

        var style = launcher.Descendants().Single(e => e.Name.LocalName == "TreeView.ItemContainerStyle");
        Assert.Contains(style.Descendants(), e => e.Name.LocalName == "Setter" &&
            (string?)e.Attribute("Property") == "IsSelected" &&
            (string?)e.Attribute("Value") == "{Binding IsSelected, Mode=TwoWay}");
    }

    [Fact]
    public void Editor_UsesNamedNativeColorMenusAndAccessibleFields()
    {
        var editor = Load("src/QuickReplyKit/Views/EditorWindow.xaml");
        foreach (string name in new[] { "NameBox", "CategoryBox", "AppBox", "TagInput", "Body" })
            Assert.False(string.IsNullOrWhiteSpace((string?)Named(editor, name).Attribute("AutomationProperties.Name")));

        Assert.DoesNotContain(editor.Descendants(), e => e.Name.LocalName == "Popup");
        Assert.True(Named(editor, "TextColorButton").Descendants().Count(e => e.Name.LocalName == "MenuItem") >= 9);
        Assert.True(Named(editor, "HighlightButton").Descendants().Count(e => e.Name.LocalName == "MenuItem") >= 7);
        Assert.Equal("Assertive", (string?)Named(editor, "ErrorText").Attribute("AutomationProperties.LiveSetting"));
        Assert.NotNull(Named(editor, "ErrorText").Attributes().SingleOrDefault(a => a.Name.LocalName == "LiveRegion.Content"));

        string behavior = File.ReadAllText(TestFiles.RepoAsset("src/QuickReplyKit/Views/LiveRegion.cs"));
        Assert.Contains("RaiseAutomationEvent(AutomationEvents.LiveRegionChanged)", behavior, StringComparison.Ordinal);
    }

    private static XDocument Load(string path) => XDocument.Load(TestFiles.RepoAsset(path));

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(e => (string?)e.Attribute(X + "Name") == name);
}
