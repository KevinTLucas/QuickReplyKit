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

        var icon = template.Descendants().Single(e => e.Name.LocalName == "TextBlock" &&
            (string?)e.Attribute("Text") == "{Binding Icon}");
        Assert.Equal("0,0,6,0", (string?)icon.Attribute("Margin"));
        Assert.DoesNotContain(template.Descendants(), e => e.Name.LocalName == "ColumnDefinition");

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

    [Fact]
    public void EditorToolbar_ExposesStateAccessibilityAndResponsiveLayout()
    {
        var editor = Load("src/QuickReplyKit/Views/EditorWindow.xaml");
        string[] toggles =
        {
            "BoldButton", "ItalicButton", "UnderlineButton", "StrikeButton",
            "AlignLeftButton", "AlignCenterButton", "AlignRightButton",
            "BulletListButton", "NumberListButton"
        };

        foreach (string name in toggles)
        {
            var toggle = Named(editor, name);
            Assert.Equal("ToggleButton", toggle.Name.LocalName);
            Assert.Equal("True", (string?)toggle.Attribute("IsThreeState"));
            Assert.False(string.IsNullOrWhiteSpace((string?)toggle.Attribute("AutomationProperties.Name")));
        }

        Assert.Equal("EditingCommands.ToggleBold", (string?)Named(editor, "BoldButton").Attribute("Command"));
        Assert.Equal("EditingCommands.ToggleItalic", (string?)Named(editor, "ItalicButton").Attribute("Command"));
        Assert.Equal("EditingCommands.ToggleUnderline", (string?)Named(editor, "UnderlineButton").Attribute("Command"));
        Assert.Equal("EditingCommands.AlignLeft", (string?)Named(editor, "AlignLeftButton").Attribute("Command"));
        Assert.Equal("EditingCommands.AlignCenter", (string?)Named(editor, "AlignCenterButton").Attribute("Command"));
        Assert.Equal("EditingCommands.AlignRight", (string?)Named(editor, "AlignRightButton").Attribute("Command"));
        Assert.Equal("EditingCommands.ToggleBullets", (string?)Named(editor, "BulletListButton").Attribute("Command"));
        Assert.Equal("EditingCommands.ToggleNumbering", (string?)Named(editor, "NumberListButton").Attribute("Command"));
        Assert.All(toggles.Where(name => name != "StrikeButton"), name =>
            Assert.Equal("{Binding ElementName=Body}", (string?)Named(editor, name).Attribute("CommandTarget")));

        var style = editor.Descendants().Single(e => e.Name.LocalName == "Style" &&
            (string?)e.Attribute(X + "Key") == "ToolToggle");
        Assert.Contains(style.Descendants(), e => e.Name.LocalName == "Trigger" &&
            (string?)e.Attribute("Property") == "IsChecked" && (string?)e.Attribute("Value") == "True");
        Assert.Contains(style.Descendants(), e => e.Name.LocalName == "Trigger" &&
            (string?)e.Attribute("Property") == "IsChecked" && (string?)e.Attribute("Value") == "{x:Null}");
        Assert.Contains(style.Descendants(), e => e.Name.LocalName == "Trigger" &&
            (string?)e.Attribute("Property") == "IsKeyboardFocused" && (string?)e.Attribute("Value") == "True");

        Assert.Equal("Body_SelectionChanged", (string?)Named(editor, "Body").Attribute("SelectionChanged"));

        var footer = Named(editor, "ErrorText").Parent!;
        Assert.Equal(2, footer.Element(footer.Name.Namespace + "Grid.ColumnDefinitions")!
            .Elements().Count());
        Assert.Equal("0", (string?)Named(editor, "ErrorText").Attribute("Grid.Column"));
        Assert.Contains(footer.Elements(), e => e.Name.LocalName == "WrapPanel" &&
            (string?)e.Attribute("Grid.Column") == "1");

        string behavior = File.ReadAllText(TestFiles.RepoAsset("src/QuickReplyKit/Views/EditorWindow.xaml.cs"));
        Assert.Contains("value == DependencyProperty.UnsetValue ? null", behavior, StringComparison.Ordinal);
        Assert.Contains("ListState(Body.Selection)", behavior, StringComparison.Ordinal);
        Assert.Contains("CommandManager.AddExecutedHandler(Body, Body_CommandExecuted)", behavior,
            StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke((Action)UpdateToolbarState)", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorColorMenus_ShowSwatchesAndReadableNames()
    {
        var editor = Load("src/QuickReplyKit/Views/EditorWindow.xaml");
        foreach (string name in new[] { "TextColorButton", "HighlightButton" })
        {
            var choices = Named(editor, name).Descendants()
                .Where(e => e.Name.LocalName == "MenuItem" && e.Attribute("Tag") is not null &&
                            (string?)e.Attribute("Tag") is not "auto" and not "none")
                .ToList();
            Assert.NotEmpty(choices);
            Assert.All(choices, choice =>
            {
                Assert.False(string.IsNullOrWhiteSpace((string?)choice.Attribute("Header")));
                Assert.False(string.IsNullOrWhiteSpace((string?)choice.Attribute("AutomationProperties.Name")));
                Assert.Contains(choice.Descendants(), e => e.Name.LocalName == "Rectangle" &&
                    (string?)e.Attribute("Fill") == (string?)choice.Attribute("Tag"));
            });
        }
    }

    private static XDocument Load(string path) => XDocument.Load(TestFiles.RepoAsset(path));

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(e => (string?)e.Attribute(X + "Name") == name);
}
