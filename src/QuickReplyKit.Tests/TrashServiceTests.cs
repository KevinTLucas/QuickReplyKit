using System.IO;
using System.Diagnostics;
using System.Threading;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Tests;

public class TrashServiceTests
{
    private static string Folder(TempDir t, string relative = "IXL") => t.Of("lib", relative);

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

    [Fact]
    public void OldFileMetadata_RemainsReadableAndRestorable()
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        string trash = t.Of("trash");
        string entry = Path.Combine(trash, "old-file");
        Directory.CreateDirectory(entry);
        TestFiles.WriteResponse(Path.Combine(entry, "Legacy.json"), html: "<p>legacy</p>");
        File.WriteAllText(Path.Combine(entry, "meta.json"),
            "{\"name\":\"Legacy\",\"originalRelativePath\":\"Old/Legacy.json\",\"deletedUtc\":\"2026-01-01T00:00:00Z\"}");
        Directory.CreateDirectory(lib);

        var item = Assert.Single(TrashService.List(trash));
        Assert.Equal(TrashItemKind.Response, item.Kind);
        Assert.Equal(TrashRestoreResult.Restored, TrashService.RestoreDetailed(item.Id, lib, trash));
        Assert.Contains("legacy", File.ReadAllText(Path.Combine(lib, "Old", "Legacy.json")));
    }

    [Fact]
    public void TrashFolder_BackupsAndRoundTripsWholeSubtree()
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        string folder = Folder(t);
        string trash = t.Of("trash");
        string backups = t.Of("backups");
        TestFiles.WriteResponse(Path.Combine(folder, "Valid.json"), html: "<p>valid</p>");
        Directory.CreateDirectory(Path.Combine(folder, "Nested"));
        File.WriteAllText(Path.Combine(folder, "Nested", "Broken.json"), "{broken");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "side file");
        File.WriteAllText(Path.Combine(folder, ".qrkfolder.json"), "{\"tags\":[\"old\"]}");

        string id = TrashService.TrashFolder(folder, lib, trash, backups);

        Assert.False(Directory.Exists(folder));
        Assert.Single(Directory.EnumerateFiles(backups, "*.zip"));
        var item = Assert.Single(TrashService.List(trash));
        Assert.Equal(id, item.Id);
        Assert.Equal(TrashItemKind.Folder, item.Kind);
        Assert.Equal("IXL", item.Name);
        Assert.Equal("IXL", item.OriginalRelativePath);
        Assert.Equal(TrashRestoreResult.Restored, TrashService.RestoreDetailed(id, lib, trash));
        Assert.Contains("valid", File.ReadAllText(Path.Combine(folder, "Valid.json")));
        Assert.Equal("{broken", File.ReadAllText(Path.Combine(folder, "Nested", "Broken.json")));
        Assert.Equal("side file", File.ReadAllText(Path.Combine(folder, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(folder, ".qrkfolder.json")));
        Assert.Empty(TrashService.List(trash));
    }

    [Fact]
    public void TrashFolder_EmptyFolderRoundTrips()
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        string folder = Folder(t, "Empty");
        string trash = t.Of("trash");
        Directory.CreateDirectory(folder);

        string id = TrashService.TrashFolder(folder, lib, trash, t.Of("backups"));

        Assert.False(Directory.Exists(folder));
        Assert.Equal(TrashItemKind.Folder, Assert.Single(TrashService.List(trash)).Kind);
        Assert.Equal(TrashRestoreResult.Restored, TrashService.RestoreDetailed(id, lib, trash));
        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
    }

    [Fact]
    public void RestoreFolder_ConflictLeavesExistingFolderAndTrashIntact()
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        string folder = Folder(t);
        string trash = t.Of("trash");
        TestFiles.WriteResponse(Path.Combine(folder, "Original.json"));
        string id = TrashService.TrashFolder(folder, lib, trash, t.Of("backups"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "occupant.txt"), "keep");

        Assert.Equal(TrashRestoreResult.Conflict, TrashService.RestoreDetailed(id, lib, trash));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(folder, "occupant.txt")));
        Assert.Single(TrashService.List(trash));
    }

    [Fact]
    public void RestoreFolder_ParentFileConflictLeavesTrashIntact()
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        string folder = t.Of("lib", "Parent", "Child");
        string trash = t.Of("trash");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "side.txt"), "safe");
        string id = TrashService.TrashFolder(folder, lib, trash, t.Of("backups"));
        Directory.Delete(Path.Combine(lib, "Parent"));
        File.WriteAllText(Path.Combine(lib, "Parent"), "occupant");

        Assert.Equal(TrashRestoreResult.Conflict, TrashService.RestoreDetailed(id, lib, trash));
        Assert.Equal("occupant", File.ReadAllText(Path.Combine(lib, "Parent")));
        Assert.Single(TrashService.List(trash));
    }

    [Fact]
    public void TrashFolder_BackupFailureLeavesFolderAndTrashUntouched()
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        string folder = Folder(t);
        string trash = t.Of("trash");
        string blocked = t.Of("not-a-directory");
        TestFiles.WriteResponse(Path.Combine(folder, "Keep.json"));
        File.WriteAllText(blocked, "blocked");

        var ex = Assert.Throws<IOException>(() => TrashService.TrashFolder(folder, lib, trash, blocked));

        Assert.Contains("required safety backup", ex.Message);
        Assert.True(Directory.Exists(folder));
        Assert.False(Directory.Exists(trash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing")]
    [InlineData("outside")]
    public void TrashFolder_RefusesRootMissingAndOutside(string which)
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        Directory.CreateDirectory(lib);
        string source = which switch
        {
            "" => lib,
            "missing" => Path.Combine(lib, "Missing"),
            _ => t.Of("Outside"),
        };
        if (which == "outside") Directory.CreateDirectory(source);

        Assert.Throws<IOException>(() => TrashService.TrashFolder(source, lib, t.Of("trash"), t.Of("backups")));
        Assert.True(Directory.Exists(lib));
        Assert.False(Directory.Exists(t.Of("trash")));
    }

    [Fact]
    public void RestoreFolder_PathTraversalIsRejectedAndTrashRemains()
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        string trash = t.Of("trash");
        string entry = Path.Combine(trash, "evil-folder");
        Directory.CreateDirectory(Path.Combine(entry, "payload"));
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(entry, "payload", "file.txt"), "safe");
        File.WriteAllText(Path.Combine(entry, "meta.json"),
            "{\"name\":\"evil\",\"originalRelativePath\":\"../outside\",\"deletedUtc\":\"2026-01-01T00:00:00Z\",\"kind\":\"folder\"}");

        Assert.Equal(TrashRestoreResult.Invalid, TrashService.RestoreDetailed("evil-folder", lib, trash));
        Assert.False(Directory.Exists(t.Of("outside")));
        Assert.Single(TrashService.List(trash));
        Assert.False(TrashService.DeleteForever("../evil-folder", trash));
    }

    [Fact]
    public void TrashFolder_RefusesNestedReparsePoint_WhenSupported()
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        string folder = Folder(t);
        string outside = t.Of("outside");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(outside);
        try { Directory.CreateSymbolicLink(Path.Combine(folder, "link"), outside); }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }
        catch (PlatformNotSupportedException) { return; }

        Assert.Throws<IOException>(() =>
            TrashService.TrashFolder(folder, lib, t.Of("trash"), t.Of("backups")));
        Assert.True(Directory.Exists(folder));
        Assert.False(Directory.Exists(t.Of("trash")));
    }

    [Fact]
    public void PurgeAndDeleteForever_WorkForFolders()
    {
        using var t = new TempDir();
        string lib = t.Of("lib");
        string trash = t.Of("trash");
        Directory.CreateDirectory(Folder(t));
        File.WriteAllText(Path.Combine(Folder(t), "side.txt"), "x");
        string first = TrashService.TrashFolder(Folder(t), lib, trash, t.Of("backups"));
        Assert.True(TrashService.DeleteForever(first, trash));
        Assert.Empty(TrashService.List(trash));

        Directory.CreateDirectory(Folder(t));
        File.WriteAllText(Path.Combine(Folder(t), "side.txt"), "x");
        TrashService.TrashFolder(Folder(t), lib, trash, t.Of("backups"));
        Assert.Equal(1, TrashService.Purge(trash, TimeSpan.Zero));
        Assert.Empty(TrashService.List(trash));
    }

    [Fact]
    public void TrashRoot_BeneathJunctionIsRejected()
    {
        using var t = new TempDir();
        string outside = t.Of("outside");
        string junction = t.Of("junction");
        Directory.CreateDirectory(outside);
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/d", "/c", "mklink", "/J", junction, outside },
        });
        process!.WaitForExit();
        if (process.ExitCode != 0) return;

        string trash = Path.Combine(junction, "trash");
        string entry = Path.Combine(outside, "trash", "entry");
        Directory.CreateDirectory(entry);
        File.WriteAllText(Path.Combine(entry, "meta.json"),
            "{\"name\":\"x\",\"originalRelativePath\":\"x.json\",\"deletedUtc\":\"2026-01-01T00:00:00Z\"}");
        File.WriteAllText(Path.Combine(entry, "x.json"), "{}");

        Assert.Empty(TrashService.List(trash));
        Assert.False(TrashService.DeleteForever("entry", trash));
        Assert.True(Directory.Exists(entry));
    }
}
