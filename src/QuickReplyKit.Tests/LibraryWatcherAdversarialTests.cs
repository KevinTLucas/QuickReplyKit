using System.IO;
using QuickReplyKit.Core.Library;

namespace QuickReplyKit.Tests;

// Time-bounded, deliberately generous waits. FileSystemWatcher is inherently
// timing-dependent; these assert coarse contract properties, not exact counts.
public class LibraryWatcherAdversarialTests
{
    private static void WriteJson(string path) =>
        File.WriteAllText(path, "{\"schema\":1,\"html\":\"<p>x</p>\"}");

    [Fact]
    public void RapidBurst_CoalescesToFewEvents()
    {
        using var t = new TempDir();
        int events = 0;
        var fired = new ManualResetEventSlim();

        using var w = new LibraryWatcher(t.Path, debounceMs: 250);
        w.Changed += () => { Interlocked.Increment(ref events); fired.Set(); };

        for (int i = 0; i < 50; i++)
            WriteJson(t.Of($"burst{i}.json"));

        Assert.True(fired.Wait(5000), "watcher never raised Changed");
        Thread.Sleep(800); // let debounce settle

        // 50 writes must collapse to a small number of debounced events.
        Assert.InRange(events, 1, 5);
    }

    [Fact]
    public void NonJsonChanges_DoNotTriggerEvents()
    {
        using var t = new TempDir();
        int events = 0;
        using var w = new LibraryWatcher(t.Path, debounceMs: 150);
        w.Changed += () => Interlocked.Increment(ref events);

        for (int i = 0; i < 10; i++)
            File.WriteAllText(t.Of($"noise{i}.log"), "noise");

        Thread.Sleep(700);
        Assert.Equal(0, events);
    }

    [Fact]
    public void Recovers_AfterRootDeletedAndRecreated()
    {
        using var t = new TempDir();
        string root = t.Of("watched");
        Directory.CreateDirectory(root);

        var fired = new ManualResetEventSlim();
        using var w = new LibraryWatcher(root, debounceMs: 150);
        w.Changed += () => fired.Set();

        // Sanity: a change is seen before the disruption.
        WriteJson(Path.Combine(root, "before.json"));
        Assert.True(fired.Wait(5000), "no event before disruption");
        fired.Reset();

        // Delete the watched root (triggers FSW Error → watcher rebuilds & recreates root).
        try { Directory.Delete(root, recursive: true); } catch { /* may be pending-delete on Windows */ }

        // Give the watcher time to notice the error and re-attach, recreating root as needed.
        // Poll: recreate the dir ourselves if it isn't back yet, then write and expect an event.
        bool recovered = false;
        for (int attempt = 0; attempt < 20 && !recovered; attempt++)
        {
            Directory.CreateDirectory(root);
            WriteJson(Path.Combine(root, $"after{attempt}.json"));
            recovered = fired.Wait(500);
        }

        Assert.True(recovered, "watcher did not recover after root delete/recreate");
    }

    [Fact]
    public void DoubleDispose_IsIdempotent()
    {
        using var t = new TempDir();
        var w = new LibraryWatcher(t.Path);
        w.Dispose();
        w.Dispose();
    }

    [Fact]
    public void NoEventsFire_AfterDispose()
    {
        using var t = new TempDir();
        int events = 0;
        var w = new LibraryWatcher(t.Path, debounceMs: 100);
        w.Changed += () => Interlocked.Increment(ref events);
        w.Dispose();

        // Writing after dispose must not resurrect events.
        for (int i = 0; i < 5; i++)
            WriteJson(t.Of($"post{i}.json"));
        Thread.Sleep(500);
        Assert.Equal(0, events);
    }
}
