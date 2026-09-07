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
    [InlineData("HotkeyManagerWindow.xaml", "SearchKeyBox")]
    [InlineData("HotkeyManagerWindow.xaml", "FilterBox")]
    [InlineData("ImportPreviewWindow.xaml", "EntryList")]
    [InlineData("InputDialog.xaml", "Input")]
    [InlineData("SettingsWindow.xaml", "HotkeyBox")]
    [InlineData("SettingsWindow.xaml", "LibraryPathBox")]
    [InlineData("SettingsWindow.xaml", "BackupPathBox")]
    [InlineData("TrashWindow.xaml", "TrashList")]
    public void PrimaryDialogControls_HaveAutomationNames(string file, string name)
    {
        var control = Named(Load(file), name);
        Assert.False(string.IsNullOrWhiteSpace((string?)control.Attribute("AutomationProperties.Name")));
    }

    [Theory]
    [InlineData("AssignHotkeyWindow.xaml", "ErrorText", "Assertive")]
    [InlineData("BackupWindow.xaml", "EmptyHint", "Polite")]
    [InlineData("HotkeyManagerWindow.xaml", "SearchResult", "Polite")]
    [InlineData("ImportPreviewWindow.xaml", "Summary", "Polite")]
    [InlineData("InputDialog.xaml", "ErrorText", "Assertive")]
    [InlineData("SettingsWindow.xaml", "HotkeyError", "Assertive")]
    [InlineData("SettingsWindow.xaml", "BackupPathError", "Assertive")]
    [InlineData("TrashWindow.xaml", "EmptyHint", "Polite")]
    public void ChangingDialogStatus_IsALiveRegion(string file, string name, string setting) =>
        Assert.Equal(setting, (string?)Named(Load(file), name).Attribute("AutomationProperties.LiveSetting"));

    [Fact]
    public void Dialogs_HaveSafeKeyboardDefaults()
    {
        AssertDefaultButton("AboutWindow.xaml", "Close");
        AssertDefaultButton("BackupWindow.xaml", "Close");
        AssertDefaultButton("TrashWindow.xaml", "_Close");
        Assert.Equal("True", (string?)Named(Load("ImportPreviewWindow.xaml"), "ImportButton").Attribute("IsDefault"));
        Assert.Equal("True", (string?)Named(Load("OnboardingWindow.xaml"), "NextButton").Attribute("IsDefault"));
        Assert.Contains("buttons.Length == 1", File.ReadAllText(TestFiles.RepoAsset(
            "src/QuickReplyKit/Views/ConfirmDialog.xaml.cs")), StringComparison.Ordinal);
    }

    private static XDocument Load(string file) => XDocument.Load(TestFiles.RepoAsset("src/QuickReplyKit/Views/" + file));

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(e => (string?)e.Attribute(X + "Name") == name);

    private static void AssertDefaultButton(string file, string content) => Assert.Contains(Load(file).Descendants(),
        e => e.Name.LocalName == "Button" && (string?)e.Attribute("Content") == content &&
             (string?)e.Attribute("IsDefault") == "True");
}
