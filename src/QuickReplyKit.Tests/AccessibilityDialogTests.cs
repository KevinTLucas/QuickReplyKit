using System.IO;
using System.Xml.Linq;

namespace QuickReplyKit.Tests;

public class AccessibilityDialogTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("AboutWindow.xaml", "DiagText")]
    [InlineData("AssignHotkeyWindow.xaml", "HotkeyBox")]
    [InlineData("BackupWindow.xaml", "BackupList")]
    [InlineData("HotkeyManagerWindow.xaml", "FilterBox")]
    [InlineData("ImportPreviewWindow.xaml", "EntryList")]
    [InlineData("InputDialog.xaml", "Input")]
    [InlineData("SettingsWindow.xaml", "HotkeyBox")]
    [InlineData("SettingsWindow.xaml", "LibraryPathBox")]
    [InlineData("SettingsWindow.xaml", "BackupPathBox")]
    [InlineData("ScriptManagerWindow.xaml", "ScriptList")]
    [InlineData("ScriptManagerWindow.xaml", "ExpressionBox")]
    [InlineData("TrashWindow.xaml", "TrashList")]
    public void PrimaryDialogControls_HaveAutomationNames(string file, string name)
    {
        var control = Named(Load(file), name);
        Assert.False(string.IsNullOrWhiteSpace((string?)control.Attribute("AutomationProperties.Name")));
    }

    [Theory]
    [InlineData("AssignHotkeyWindow.xaml", "ErrorText", "Assertive")]
    [InlineData("BackupWindow.xaml", "EmptyHint", "Polite")]
    [InlineData("ImportPreviewWindow.xaml", "SelectionSummary", "Polite")]
    [InlineData("InputDialog.xaml", "ErrorText", "Assertive")]
    [InlineData("SettingsWindow.xaml", "HotkeyError", "Assertive")]
    [InlineData("SettingsWindow.xaml", "BackupPathError", "Assertive")]
    [InlineData("ScriptManagerWindow.xaml", "StatusText", "Assertive")]
    [InlineData("TrashWindow.xaml", "EmptyHint", "Polite")]
    public void ChangingDialogStatus_IsALiveRegion(string file, string name, string setting)
    {
        var element = Named(Load(file), name);
        Assert.Equal(setting, (string?)element.Attribute("AutomationProperties.LiveSetting"));
        Assert.Contains(element.Attributes(), a => a.Name.LocalName == "LiveRegion.Content");
    }

    [Fact]
    public void Dialogs_HaveSafeKeyboardDefaults()
    {
        AssertDefaultButton("BackupWindow.xaml", "Close");
        AssertDefaultButton("TrashWindow.xaml", "_Close");
        Assert.Equal("True", (string?)Named(Load("ImportPreviewWindow.xaml"), "ImportButton").Attribute("IsDefault"));
        Assert.Equal("True", (string?)Named(Load("OnboardingWindow.xaml"), "NextButton").Attribute("IsDefault"));
        Assert.Contains("buttons.Length == 1", File.ReadAllText(TestFiles.RepoAsset(
            "src/QuickReplyKit/Views/ConfirmDialog.xaml.cs")), StringComparison.Ordinal);

        var import = Load("ImportPreviewWindow.xaml").Root!;
        Assert.Equal("CanResizeWithGrip", (string?)import.Attribute("ResizeMode"));
        Assert.True(double.Parse((string)import.Attribute("Width")!) <= 760);
        Assert.True(double.Parse((string)import.Attribute("MinWidth")!) <= 520);
        Assert.True(double.Parse((string)import.Attribute("Height")!) <= 540);

        var scripts = Load("ScriptManagerWindow.xaml").Root!;
        Assert.Equal("CanResizeWithGrip", (string?)scripts.Attribute("ResizeMode"));
        Assert.DoesNotContain(Load("ScriptManagerWindow.xaml").Descendants(), e =>
            ((string?)e.Attribute("Content"))?.Contains("all", StringComparison.OrdinalIgnoreCase) == true &&
            ((string?)e.Attribute("Content"))?.Contains("trust", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Theory]
    [InlineData("TrashWindow.xaml", "TrashList")]
    [InlineData("BackupWindow.xaml", "BackupList")]
    public void CollectionDialogs_ResizeAndListsUseAvailableHeight(string file, string listName)
    {
        var document = Load(file);
        var window = document.Root!;

        Assert.Equal("CanResizeWithGrip", (string?)window.Attribute("ResizeMode"));
        Assert.NotNull(window.Attribute("MinWidth"));
        Assert.NotNull(window.Attribute("MinHeight"));
        Assert.Null(Named(document, listName).Attribute("Height"));
    }

    [Theory]
    [InlineData("TrashWindow.xaml", "Name")]
    [InlineData("TrashWindow.xaml", "Sub")]
    [InlineData("HotkeyManagerWindow.xaml", "Target")]
    public void EllipsizedCollectionText_ExposesFullValueInTooltip(string file, string binding)
    {
        var text = Load(file).Descendants().Single(e => e.Name.LocalName == "TextBlock" &&
            (string?)e.Attribute("Text") == $"{{Binding {binding}}}" &&
            (string?)e.Attribute("TextTrimming") == "CharacterEllipsis");

        Assert.Equal($"{{Binding {binding}}}", (string?)text.Attribute("ToolTip"));
    }

    [Fact]
    public void AboutDiagnostics_HasKeyboardOnlyThemedFocusCue()
    {
        var border = Named(Load("AboutWindow.xaml"), "DiagnosticsBorder");
        var trigger = border.Descendants().Single(e => e.Name.LocalName == "DataTrigger");

        Assert.Contains("IsKeyboardFocusWithin", (string?)trigger.Attribute("Binding"));
        Assert.Equal("True", (string?)trigger.Attribute("Value"));
        Assert.Contains(trigger.Descendants(), e => e.Name.LocalName == "Setter" &&
            (string?)e.Attribute("Property") == "BorderBrush" &&
            (string?)e.Attribute("Value") == "{DynamicResource Accent}");
        Assert.DoesNotContain(border.Descendants(), e => e.Name.LocalName == "Trigger" &&
            (string?)e.Attribute("Property") == "IsMouseOver");
    }

    private static XDocument Load(string file) => XDocument.Load(TestFiles.RepoAsset("src/QuickReplyKit/Views/" + file));

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(e => (string?)e.Attribute(X + "Name") == name);

    private static void AssertDefaultButton(string file, string content) => Assert.Contains(Load(file).Descendants(),
        e => e.Name.LocalName == "Button" && (string?)e.Attribute("Content") == content &&
             (string?)e.Attribute("IsDefault") == "True");
}
