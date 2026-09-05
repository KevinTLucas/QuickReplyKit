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
    public void Watcher_DisposeIsClean()
    {
        using var t = new TempDir();
        var w = new LibraryWatcher(t.Path);
        w.Dispose();
        w.Dispose(); // idempotent, no throw
    }
}
