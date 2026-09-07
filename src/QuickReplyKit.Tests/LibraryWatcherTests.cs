using System.IO;
using QuickReplyKit.Core.Library;

namespace QuickReplyKit.Tests;

public class LibraryWatcherTests
{
    [Fact]
    public void Watcher_Debounces_BurstOfChanges_IntoFewerEvents()
    {
        using var t = new TempDir();
        int events = 0;
        var fired = new ManualResetEventSlim();

        using var w = new LibraryWatcher(t.Path, debounceMs: 200);
        w.Changed += () => { Interlocked.Increment(ref events); fired.Set(); };

        for (int i = 0; i < 10; i++)
            File.WriteAllText(t.Of($"f{i}.json"), "{\"schema\":1,\"html\":\"<p>x</p>\"}");

        Assert.True(fired.Wait(5000), "watcher never raised Changed");
        Thread.Sleep(600); // let any further debounced events settle

        Assert.True(events >= 1);
        Assert.True(events < 10, $"expected debouncing, got {events} events");
    }

    [Fact]
    public void Watcher_AbsentRootWithMissingParent_DoesNotThrowOrFabricateFolder()
    {
        using var t = new TempDir();
        // A deep path whose parent doesn't exist (mimics a temporarily-offline OneDrive mount).
        string parent = t.Of("offline_mount");
        string root = Path.Combine(parent, "Library");

        int events = 0;
        // Constructing must not throw, must not crash, and must NOT fabricate the local tree.
        using var w = new LibraryWatcher(root, debounceMs: 100);
        w.Changed += () => Interlocked.Increment(ref events);

        Assert.False(w.IsAttached);
        Assert.False(Directory.Exists(parent), "watcher fabricated an offline mount's parent folder");
        Assert.False(Directory.Exists(root), "watcher fabricated the offline library folder");
        Thread.Sleep(200);
        Assert.Equal(0, events); // nothing to watch; stays quiet, no throw
    }

    [Fact]
    public void Watcher_NormalLocalPath_StillWatches_AndFiresDebouncedChanged()
    {
        using var t = new TempDir();
        string root = t.Of("lib"); // parent (t.Path) exists → normal local library
        var fired = new ManualResetEventSlim();

        using var w = new LibraryWatcher(root, debounceMs: 150);
        w.Changed += () => fired.Set();

        Assert.True(w.IsAttached);
        Assert.True(Directory.Exists(root), "watcher should create a library folder under an existing parent");
        File.WriteAllText(Path.Combine(root, "x.json"), "{\"schema\":1,\"html\":\"<p>x</p>\"}");

        Assert.True(fired.Wait(5000), "watcher never raised Changed for a normal local path");
    }

    [Fact]
    public void Watcher_DisposeIsClean()
    {
        using var t = new TempDir();
        var w = new LibraryWatcher(t.Path);
        Assert.True(w.IsAttached);
        w.Dispose();
        Assert.False(w.IsAttached);
        w.Dispose(); // idempotent, no throw
    }

    [Fact]
    public void IsAttached_DoesNotWaitForChangedHandler()
    {
        using var t = new TempDir();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var w = new LibraryWatcher(t.Path, debounceMs: 50);
        w.Changed += () => { entered.Set(); release.Wait(5000); };
        File.WriteAllText(t.Of("x.json"), "{\"schema\":1,\"html\":\"<p>x</p>\"}");
        Assert.True(entered.Wait(5000));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(w.IsAttached);
        sw.Stop();
        release.Set();

        Assert.True(sw.ElapsedMilliseconds < 100, $"status read blocked for {sw.ElapsedMilliseconds} ms");
    }
}
