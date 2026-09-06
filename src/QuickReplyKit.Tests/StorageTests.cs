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
