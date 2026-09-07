using System.IO;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Tests;

public class AppConfigTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        using var t = new TempDir();
        var cfg = AppConfig.Load(t.Of("nope.json"));

        Assert.Equal("Ctrl+Alt+D", cfg.HotkeyGesture);
        Assert.Equal(AppPaths.DefaultLibraryDir, cfg.LibraryPath);
        Assert.Equal(PasteMode.RichText, cfg.DefaultPasteMode);
        Assert.True(cfg.RememberWindow);   // on by default
        Assert.NotNull(cfg.Window);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        using var t = new TempDir();
        string path = t.Of("config.json");
        var cfg = new AppConfig
        {
            HotkeyGesture = "Ctrl+Shift+Q",
            LibraryPath = @"C:\Custom\Lib",
            DefaultPasteMode = PasteMode.PlainText,
            RememberWindow = false,
            Window = new WindowBounds { X = 1, Y = 2, Width = 300, Height = 400, MonitorDeviceName = @"\\.\DISPLAY1" },
        };
        cfg.Save(path);

        var loaded = AppConfig.Load(path);
        Assert.Equal("Ctrl+Shift+Q", loaded.HotkeyGesture);
        Assert.Equal(@"C:\Custom\Lib", loaded.LibraryPath);
        Assert.Equal(PasteMode.PlainText, loaded.DefaultPasteMode);
        Assert.False(loaded.RememberWindow);
        Assert.Equal(300, loaded.Window.Width);
        Assert.Equal(400, loaded.Window.Height);
        Assert.Equal(@"\\.\DISPLAY1", loaded.Window.MonitorDeviceName);

        // Enum stored as a readable string, not a number.
        Assert.Contains("PlainText", File.ReadAllText(path));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        using var t = new TempDir();
        string path = t.Of("config.json");
        File.WriteAllText(path, "{ not valid json ");

        var loaded = AppConfig.Load(path);
        Assert.Equal("Ctrl+Alt+D", loaded.HotkeyGesture);
    }

    [Fact]
    public void Theme_DefaultsToSystem()
    {
        using var t = new TempDir();
        Assert.Equal(AppTheme.System, AppConfig.Load(t.Of("nope.json")).Theme);
    }

    [Fact]
    public void Theme_RoundTrips_AsReadableString()
    {
        using var t = new TempDir();
        string path = t.Of("config.json");
        new AppConfig { Theme = AppTheme.Dark }.Save(path);

        Assert.Equal(AppTheme.Dark, AppConfig.Load(path).Theme);
        Assert.Contains("Dark", File.ReadAllText(path));   // enum stored by name, not number
    }

    [Fact]
    public void ResponseHotkeys_DefaultEmpty()
    {
        using var t = new TempDir();
        Assert.Empty(AppConfig.Load(t.Of("nope.json")).ResponseHotkeys);
    }

    [Fact]
    public void ResponseHotkeys_RoundTrip()
    {
        using var t = new TempDir();
        string path = t.Of("config.json");
        var cfg = new AppConfig();
        cfg.ResponseHotkeys.Add(new ResponseHotkey { RelativePath = "IXL/Greeting.json", Gesture = "Ctrl+Alt+1", Plain = false });
        cfg.ResponseHotkeys.Add(new ResponseHotkey { RelativePath = "Billing/Refund.json", Gesture = "Ctrl+Alt+2", Plain = true });
        cfg.Save(path);

        var loaded = AppConfig.Load(path);
        Assert.Equal(2, loaded.ResponseHotkeys.Count);
        Assert.Equal("IXL/Greeting.json", loaded.ResponseHotkeys[0].RelativePath);
        Assert.Equal("Ctrl+Alt+1", loaded.ResponseHotkeys[0].Gesture);
        Assert.False(loaded.ResponseHotkeys[0].Plain);
        Assert.Equal("Billing/Refund.json", loaded.ResponseHotkeys[1].RelativePath);
        Assert.True(loaded.ResponseHotkeys[1].Plain);
    }
}

public class ThemeResolverTests
{
    [Theory]
    [InlineData(AppTheme.Light, true, AppTheme.Light)]
    [InlineData(AppTheme.Light, false, AppTheme.Light)]
    [InlineData(AppTheme.Dark, true, AppTheme.Dark)]
    [InlineData(AppTheme.Dark, false, AppTheme.Dark)]
    [InlineData(AppTheme.System, true, AppTheme.Light)]   // System follows the OS flag
    [InlineData(AppTheme.System, false, AppTheme.Dark)]
    public void Effective_ResolvesPreferenceAndSystemFlag(AppTheme pref, bool systemLight, AppTheme expected)
        => Assert.Equal(expected, ThemeResolver.Effective(pref, systemLight));
}

public class RecentStoreTests
{
    [Fact]
    public void Load_MissingFile_IsEmpty()
    {
        using var t = new TempDir();
        Assert.Empty(RecentStore.Load(t.Of("nope.json")).Items);
    }

    [Fact]
    public void Save_Then_Load_PreservesOrder()
    {
        using var t = new TempDir();
        string path = t.Of("recent.json");
        var store = new RecentStore();
        store.Add("a.json");
        store.Add("b.json");
        store.Save(path);

        var loaded = RecentStore.Load(path);
        Assert.Equal(new[] { "b.json", "a.json" }, loaded.Items.Select(i => i.RelativePath));
        Assert.All(loaded.Items, i => Assert.NotEqual(default, i.UsedAt));
    }

    [Fact]
    public void Add_CapsAndDeduplicates_MovingToFront()
    {
        var store = new RecentStore(cap: 3);
        store.Add("a");
        store.Add("b");
        store.Add("c");
        store.Add("d"); // evicts "a"

        Assert.Equal(new[] { "d", "c", "b" }, store.Items.Select(i => i.RelativePath));

        store.Add("c"); // existing → moves to front, no growth
        Assert.Equal(new[] { "c", "d", "b" }, store.Items.Select(i => i.RelativePath));
        Assert.Equal(3, store.Items.Count);
    }

    [Fact]
    public void Load_CorruptFile_IsEmpty()
    {
        using var t = new TempDir();
        string path = t.Of("recent.json");
        File.WriteAllText(path, "not json at all");
        Assert.Empty(RecentStore.Load(path).Items);
    }
}
