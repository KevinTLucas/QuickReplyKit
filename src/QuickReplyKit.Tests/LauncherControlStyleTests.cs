using System.Xml.Linq;

namespace QuickReplyKit.Tests;

public class LauncherControlStyleTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SharedButton_ReservesStrongOutlinesForKeyboardFocus()
    {
        var style = Named(Load("src/QuickReplyKit/App.xaml"), "ThemedButton");
        var hover = Trigger(style, "IsMouseOver");
        var pressed = Trigger(style, "IsPressed");
        var focus = Trigger(style, "IsKeyboardFocused");

        Assert.Contains(hover.Descendants(), e => Setter(e, "State", "Background", "{DynamicResource ButtonHoverOverlay}"));
        Assert.Contains(pressed.Descendants(), e => Setter(e, "State", "Background", "{DynamicResource ButtonPressedOverlay}"));
        Assert.DoesNotContain(hover.Descendants(), IsOutlineSetter);
        Assert.DoesNotContain(pressed.Descendants(), IsOutlineSetter);
        Assert.Contains(focus.Descendants(), e => Setter(e, "StateOutlineOuter", "BorderThickness", "2"));
        Assert.Contains(focus.Descendants(), e => Setter(e, "StateOutlineInner", "BorderThickness", "1"));
    }

    [Fact]
    public void Launcher_GlyphTargetsAreStableAndFavoriteHoverIsUnboxed()
    {
        var launcher = Load("src/QuickReplyKit/Views/LauncherWindow.xaml");
        var iconStyle = Named(launcher, "IconButton");
        var favoriteStyle = Named(launcher, "FavoriteToggle");
        var clearScope = launcher.Descendants().Single(e =>
            (string?)e.Attribute("AutomationProperties.Name") == "Remove folder search filter");

        AssertSetter(iconStyle, "MinWidth", "32");
        AssertSetter(iconStyle, "MinHeight", "32");
        AssertSetter(favoriteStyle, "MinWidth", "28");
        AssertSetter(favoriteStyle, "MinHeight", "28");
        Assert.DoesNotContain(favoriteStyle.Descendants(), e =>
            e.Name.LocalName == "Trigger" && (string?)e.Attribute("Property") == "IsMouseOver");
        Assert.NotNull(Trigger(favoriteStyle, "IsKeyboardFocused"));
        Assert.Equal("28", (string?)clearScope.Attribute("MinWidth"));
        Assert.Equal("28", (string?)clearScope.Attribute("MinHeight"));
    }

    [Fact]
    public void Launcher_PreviewActionsWrapAtCompactWidths()
    {
        var launcher = Load("src/QuickReplyKit/Views/LauncherWindow.xaml");
        var actions = launcher.Descendants().Single(e => e.Name.LocalName == "WrapPanel" &&
            e.Elements().Count(b => b.Name.LocalName == "Button" &&
                (string?)b.Attribute("Style") == "{StaticResource ActionButton}") == 4);

        Assert.Equal(new[] { "Paste", "Paste plain", "Copy", "✎ Edit" },
            actions.Elements().Where(e => e.Name.LocalName == "Button")
                .Select(e => (string?)e.Attribute("Content")));
    }

    private static XDocument Load(string path) => XDocument.Load(TestFiles.RepoAsset(path));

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(e => (string?)e.Attribute(X + "Key") == name || (string?)e.Attribute(X + "Name") == name);

    private static XElement Trigger(XElement root, string property) => root.Descendants()
        .Single(e => e.Name.LocalName == "Trigger" && (string?)e.Attribute("Property") == property);

    private static bool Setter(XElement element, string target, string property, string value) =>
        element.Name.LocalName == "Setter" && (string?)element.Attribute("TargetName") == target &&
        (string?)element.Attribute("Property") == property && (string?)element.Attribute("Value") == value;

    private static bool IsOutlineSetter(XElement element) => element.Name.LocalName == "Setter" &&
        ((string?)element.Attribute("TargetName"))?.StartsWith("StateOutline", StringComparison.Ordinal) == true;

    private static void AssertSetter(XElement style, string property, string value) => Assert.Contains(
        style.Elements(), e => e.Name.LocalName == "Setter" &&
            (string?)e.Attribute("Property") == property && (string?)e.Attribute("Value") == value);
}
