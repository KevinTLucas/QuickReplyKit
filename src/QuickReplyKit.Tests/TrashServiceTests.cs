using System.IO;
using System.Threading;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Tests;

public class TrashServiceTests
{
    [Fact]
    public void Trash_MovesFileOut_AndListsIt()
    {
        using var lib = new TempDir();
        var trash = lib.Of("..", "trash");
        var file = Path.Combine(lib.Path, "IXL", "Hi.json");
        TestFiles.WriteResponse(file);

        string id = TrashService.Trash(file, lib.Path, trash);

        Assert.False(File.Exists(file));                 // moved out of the library
        var items = TrashService.List(trash);
        Assert.Single(items);
        Assert.Equal(id, items[0].Id);
        Assert.Equal("Hi", items[0].Name);
        Assert.Equal("IXL/Hi.json", items[0].OriginalRelativePath);
        Directory.Delete(trash, true);
    }

    [Fact]
    public void Restore_PutsFileBack_AtOriginalPath()
    {
        using var lib = new TempDir();
        var trash = lib.Of("..", "trash");
        var file = Path.Combine(lib.Path, "IXL", "Hi.json");
        TestFiles.WriteResponse(file, html: "<p>keep me</p>");

        string id = TrashService.Trash(file, lib.Path, trash);
        Assert.True(TrashService.Restore(id, lib.Path, trash));

        Assert.True(File.Exists(file));
        Assert.Contains("keep me", File.ReadAllText(file));
        Assert.Empty(TrashService.List(trash));          // gone from trash after restore
        Directory.Delete(trash, true);
    }

    [Fact]
    public void Restore_Conflict_LeavesItemInTrash_AndReturnsFalse()
    {
        using var lib = new TempDir();
        var trash = lib.Of("..", "trash");
        var file = Path.Combine(lib.Path, "Hi.json");
        TestFiles.WriteResponse(file);
        string id = TrashService.Trash(file, lib.Path, trash);

        TestFiles.WriteResponse(file, html: "<p>new occupant</p>");   // something else now at that path
        Assert.False(TrashService.Restore(id, lib.Path, trash));
        Assert.Single(TrashService.List(trash));                      // still recoverable
        Assert.Contains("new occupant", File.ReadAllText(file));      // existing file untouched
        Directory.Delete(trash, true);
    }

    [Fact]
    public void Purge_RemovesOnlyItemsOlderThanRetention()
    {
        using var lib = new TempDir();
        var trash = lib.Of("..", "trash");
        var f1 = Path.Combine(lib.Path, "Old.json");
        TestFiles.WriteResponse(f1);
        TrashService.Trash(f1, lib.Path, trash);
        Thread.Sleep(30);

        // Retention of 0 → everything is "older than now" and purged; a huge retention keeps all.
        Assert.Equal(0, TrashService.Purge(trash, TimeSpan.FromDays(365)));
        Assert.Single(TrashService.List(trash));
        Assert.Equal(1, TrashService.Purge(trash, TimeSpan.Zero));
        Assert.Empty(TrashService.List(trash));
        Directory.Delete(trash, true);
    }

    [Fact]
    public void Restore_RefusesToEscapeLibraryRoot()
    {
        using var lib = new TempDir();
        var trash = lib.Of("..", "trash");
        // Hand-craft a malicious trash entry whose original path tries to climb out of the library.
        var id = "evil";
        var dir = Path.Combine(trash, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pwn.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "meta.json"),
            "{\"name\":\"pwn\",\"originalRelativePath\":\"../pwned.json\",\"deletedUtc\":\"2026-01-01T00:00:00Z\"}");

        Assert.False(TrashService.Restore(id, lib.Path, trash));
        Assert.False(File.Exists(Path.Combine(lib.Path, "..", "pwned.json")));   // nothing written outside root
        Directory.Delete(trash, true);
    }

    [Fact]
    public void Empty_RemovesAll()
    {
        using var lib = new TempDir();
        var trash = lib.Of("..", "trash");
        foreach (var n in new[] { "A.json", "B.json" })
        {
            var f = Path.Combine(lib.Path, n);
            TestFiles.WriteResponse(f);
            TrashService.Trash(f, lib.Path, trash);
        }
        Assert.Equal(2, TrashService.List(trash).Count);
        TrashService.Empty(trash);
        Assert.Empty(TrashService.List(trash));
        Directory.Delete(trash, true);
    }
}
