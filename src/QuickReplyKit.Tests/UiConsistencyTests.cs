using System.IO;
using System.Xml.Linq;

namespace QuickReplyKit.Tests;

public class UiConsistencyTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("AssignHotkeyWindow.xaml.cs")]
    [InlineData("SettingsWindow.xaml.cs")]
    public void HotkeyCapture_LeavesDialogNavigationAndWindowKeysUnhandled(string file)
    {
        string code = File.ReadAllText(View(file));
        int bypass = code.IndexOf("key is Key.Tab or Key.Escape", StringComparison.Ordinal);
        int handled = code.IndexOf("e.Handled = true;", bypass, StringComparison.Ordinal);

        Assert.True(bypass >= 0 && handled > bypass);
        Assert.Contains("key == Key.F4", code, StringComparison.Ordinal);
        Assert.Contains("ModifierKeys.Alt", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TallResizableDialogs_LimitHeightToTheirCurrentMonitor()
    {
        string helper = File.ReadAllText(View("WindowSizing.cs"));
        Assert.Contains("Screen.FromHandle", helper, StringComparison.Ordinal);
        Assert.Contains("VisualTreeHelper.GetDpi", helper, StringComparison.Ordinal);
        Assert.Contains("LocationChanged", helper, StringComparison.Ordinal);
        Assert.Contains("DpiChanged", helper, StringComparison.Ordinal);

        foreach (string file in new[]
        {
            "AboutWindow.xaml.cs", "EditorWindow.xaml.cs", "HotkeyManagerWindow.xaml.cs",
            "ImportPreviewWindow.xaml.cs", "ScriptManagerWindow.xaml.cs", "SettingsWindow.xaml.cs",
        })
            Assert.Contains("WindowSizing.LimitHeightToCurrentScreen(this);",
                File.ReadAllText(View(file)), StringComparison.Ordinal);

        Assert.Equal("CanResizeWithGrip", (string?)Load("EditorWindow.xaml").Root!.Attribute("ResizeMode"));
        Assert.Equal("CanResizeWithGrip", (string?)Load("HotkeyManagerWindow.xaml").Root!.Attribute("ResizeMode"));
    }

    [Theory]
    [InlineData("TrashWindow.xaml", "{Binding Name}", "{Binding Sub}")]
    [InlineData("ScriptManagerWindow.xaml", "{Binding Name}", "{Binding Status}")]
    [InlineData("ImportPreviewWindow.xaml", "{Binding DestinationPath}", "{Binding Detail}")]
    public void CollectionRows_ExposeItemLevelAutomationText(string file, string name, string help)
    {
        var style = Load(file).Descendants().Single(e => e.Name.LocalName == "Style" &&
            (string?)e.Attribute("TargetType") == "ListBoxItem" &&
            e.Elements().Any(s => (string?)s.Attribute("Property") == "AutomationProperties.Name"));
        Assert.Contains(style.Elements(), s => (string?)s.Attribute("Property") == "AutomationProperties.Name" &&
            (string?)s.Attribute("Value") == name);
        Assert.Contains(style.Elements(), s => (string?)s.Attribute("Property") == "AutomationProperties.HelpText" &&
            (string?)s.Attribute("Value") == help);
    }

    [Theory]
    [InlineData("LauncherWindow.xaml", "Resize navigation pane")]
    [InlineData("LauncherWindow.xaml", "Resize response preview")]
    [InlineData("ScriptManagerWindow.xaml", "Resize script list and editor")]
    public void KeyboardReachableSplitters_HaveAutomationNames(string file, string name) =>
        Assert.Contains(Load(file).Descendants(), e => e.Name.LocalName == "GridSplitter" &&
            (string?)e.Attribute("AutomationProperties.Name") == name);

    [Fact]
    public void HotkeyManager_FilterAndClippedValuesStayUnderstandable()
    {
        var document = Load("HotkeyManagerWindow.xaml");
        var label = Named(document, "FilterLabel");
        var filter = Named(document, "FilterBox");
        Assert.Equal("Filter by shortcut or response", (string?)label.Attribute("Text"));
        Assert.Equal("{Binding ElementName=FilterLabel}", (string?)filter.Attribute("AutomationProperties.LabeledBy"));

        Assert.Contains(document.Descendants(), e => (string?)e.Attribute("Text") == "{Binding Gesture}" &&
            (string?)e.Attribute("ToolTip") == "{Binding Gesture}");
        Assert.Contains(document.Descendants(), e => (string?)e.Attribute(X + "Name") == "StatusText" &&
            (string?)e.Attribute("TextWrapping") == "Wrap" &&
            (string?)e.Attribute("ToolTip") == "{Binding Status}");

        var row = document.Descendants().Single(e => e.Name.LocalName == "ContentControl" &&
            (string?)e.Attribute("AutomationProperties.Name") == "{Binding Target}");
        Assert.Equal("{Binding Status}", (string?)row.Attribute("AutomationProperties.HelpText"));
        foreach (string action in new[] { "Edit", "Clear" })
            Assert.Contains(document.Descendants(), e => e.Name.LocalName == "Button" &&
                (string?)e.Attribute("Content") == action &&
                ((string?)e.Attribute("AutomationProperties.Name"))?.Contains("{Binding Target", StringComparison.Ordinal) == true &&
                (string?)e.Attribute("AutomationProperties.HelpText") == "{Binding Status}");
    }

    [Fact]
    public void EllipsizedDialogValues_ExposeTheirFullText()
    {
        var assign = Named(Load("AssignHotkeyWindow.xaml"), "ResponseName");
        Assert.Equal("CharacterEllipsis", (string?)assign.Attribute("TextTrimming"));
        Assert.Equal("{Binding Text, RelativeSource={RelativeSource Self}}", (string?)assign.Attribute("ToolTip"));

        var settings = Load("SettingsWindow.xaml");
        foreach (string name in new[] { "LibraryPathBox", "BackupPathBox" })
            Assert.Equal("{Binding Text, RelativeSource={RelativeSource Self}}",
                (string?)Named(settings, name).Attribute("ToolTip"));

        var scripts = Load("ScriptManagerWindow.xaml");
        foreach (string binding in new[] { "Name", "Status", "RelativePath" })
            Assert.Contains(scripts.Descendants(), e =>
                (string?)e.Attribute("Text") == $"{{Binding {binding}}}" &&
                (string?)e.Attribute("TextTrimming") == "CharacterEllipsis" &&
                (string?)e.Attribute("ToolTip") == $"{{Binding {binding}}}");
    }

    [Fact]
    public void EditorDropdownMenus_OpenBelowTheirTrigger()
    {
        var document = Load("EditorWindow.xaml");
        foreach (string name in new[] { "SizeButton", "TextColorButton", "HighlightButton", "InsertVarButton" })
            Assert.Equal("Bottom", (string?)Named(document, name).Descendants()
                .Single(e => e.Name.LocalName == "ContextMenu").Attribute("Placement"));

        var app = XDocument.Load(TestFiles.RepoAsset("src/QuickReplyKit/App.xaml"));
        foreach (string target in new[] { "{x:Type ContextMenu}", "{x:Type MenuItem}" })
            Assert.Contains(app.Descendants(), e => e.Name.LocalName == "Style" &&
                (string?)e.Attribute("TargetType") == target &&
                e.Elements().Any(s => (string?)s.Attribute("Property") == "Template"));
    }

    [Theory]
    [InlineData("InputDialog.xaml", "Cancel", "OK")]
    [InlineData("SettingsWindow.xaml", "Cancel", "Save")]
    public void CommitDialogs_PutAccentPrimaryActionLast(string file, string cancel, string primary)
    {
        var buttons = Load(file).Descendants().Where(e => e.Name.LocalName == "Button").ToList();
        int cancelIndex = buttons.FindIndex(e => (string?)e.Attribute("Content") == cancel);
        int primaryIndex = buttons.FindIndex(e => (string?)e.Attribute("Content") == primary);
        Assert.True(cancelIndex >= 0 && primaryIndex > cancelIndex);
        Assert.Equal("{StaticResource PrimaryButton}", (string?)buttons[primaryIndex].Attribute("Style"));
        Assert.Equal("True", (string?)buttons[primaryIndex].Attribute("IsDefault"));
    }

    [Fact]
    public void AssignHotkey_PutsCancelBeforeAccentSave()
    {
        var buttons = Load("AssignHotkeyWindow.xaml").Descendants()
            .Where(e => e.Name.LocalName == "Button").ToList();
        Assert.Equal(new[] { "Remove", "Cancel", "Save" }, buttons.Select(e => (string?)e.Attribute("Content")));
        Assert.Equal("{StaticResource PrimaryButton}", (string?)buttons[^1].Attribute("Style"));
    }

    [Fact]
    public void BackupWindow_SeparatesCloseFromOperationalActions()
    {
        var document = Load("BackupWindow.xaml");
        var close = document.Descendants().Single(e =>
            e.Name.LocalName == "Button" && (string?)e.Attribute("Content") == "Close");
        Assert.Equal("1", (string?)close.Attribute("Grid.Column"));
        Assert.Equal("Right", (string?)close.Attribute("HorizontalAlignment"));
        Assert.Equal("True", (string?)close.Attribute("IsCancel"));
        Assert.Equal("True", (string?)close.Attribute("IsDefault"));

        var footer = close.Parent!;
        Assert.Equal(new[] { "*", "Auto" }, footer.Elements()
            .Single(e => e.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().Select(e => (string?)e.Attribute("Width")));
    }

    [Theory]
    [InlineData("--show-editor", "EditorWindow.OpenNew")]
    [InlineData("--show-trash", "new Views.TrashWindow")]
    public void QaDialogRoutes_ShowTheirOwnerBeforeOpening(string argument, string dialogCall)
    {
        string code = File.ReadAllText(TestFiles.RepoAsset("src/QuickReplyKit/App.xaml.cs"));
        int route = code.IndexOf(argument, StringComparison.Ordinal);
        int show = code.IndexOf("_launcher!.Show();", route, StringComparison.Ordinal);
        int open = code.IndexOf(dialogCall, route, StringComparison.Ordinal);

        Assert.True(route >= 0 && show > route && open > show);
    }

    [Fact]
    public void Settings_ScriptDescriptionDescribesRestrictedExpressions()
    {
        var button = Load("SettingsWindow.xaml").Descendants().Single(e =>
            (string?)e.Attribute("Click") == "ManageScripts_Click");
        Assert.Equal("Manage template scripts", (string?)button.Attribute("AutomationProperties.Name"));
        Assert.Contains("restricted template expressions", (string?)button.Attribute("ToolTip"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void About_LabelsRawTrashFolderActionAccurately() =>
        Assert.Contains(Load("AboutWindow.xaml").Descendants(), e => e.Name.LocalName == "Button" &&
            (string?)e.Attribute("Click") == "OpenTrash_Click" &&
            (string?)e.Attribute("Content") == "Open trash folder");

    private static string View(string file) => TestFiles.RepoAsset("src/QuickReplyKit/Views/" + file);
    private static XDocument Load(string file) => XDocument.Load(View(file));
    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(e => (string?)e.Attribute(X + "Name") == name);
}
