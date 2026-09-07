using System.IO;
using System.IO.Compression;
using QuickReplyKit.Core.ImportExport;

namespace QuickReplyKit.Tests;

public class LibraryBackupTests
{
    [Fact]
    public void Create_EmptyOrMissingLibrary_ReturnsNull()
    {
        using var t = new TempDir();
        var lib = t.Of("lib");
        Assert.Null(LibraryBackup.Create(lib, t.Of("backups"), t.Of("config.json"), null));
        Directory.CreateDirectory(lib);
        Assert.Null(LibraryBackup.Create(lib, t.Of("backups"), t.Of("config.json"), null));
    }

    [Fact]
    public void Create_SnapshotsLibraryAndConfig_InVersionedLayout()
    {
        using var t = new TempDir();
        var lib = t.Of("lib");
        var config = t.Of("config.json");
        TestFiles.WriteResponse(Path.Combine(lib, "IXL", "Hi.json"));
        File.WriteAllText(config, """{"theme":"Dark"}""");

        var path = LibraryBackup.Create(lib, t.Of("backups"), config, "test");

        Assert.NotNull(path);
        using var zip = ZipFile.OpenRead(path!);
        Assert.Contains(zip.Entries, e => e.FullName == "quickreplykit-backup.json");
        Assert.Contains(zip.Entries, e => e.FullName == "library/IXL/Hi.json");
        Assert.Contains(zip.Entries, e => e.FullName == "config.json");
    }

    [Fact]
    public void Create_PrunesToProductionCountLimit()
    {
        using var t = new TempDir();
        var lib = t.Of("lib");
        TestFiles.WriteResponse(Path.Combine(lib, "a.json"));
        var backups = t.Of("backups");
        for (int i = 0; i < LibraryBackup.MaxKeep + 3; i++)
            LibraryBackup.Create(lib, backups, t.Of("config.json"), null);

        Assert.Equal(LibraryBackup.MaxKeep, LibraryBackup.List(backups).Count);
    }

    [Fact]
    public void Prune_EnforcesCountSizeAndAgeBounds()
    {
        using var t = new TempDir();
        var now = new DateTime(2026, 9, 7, 12, 0, 0);

        string count = t.Of("count");
        WriteBackups(count, now, 5, 4);
        LibraryBackup.Prune(count, 2, 100, TimeSpan.FromDays(30), now);
        Assert.Equal(2, LibraryBackup.List(count).Count);

        string size = t.Of("size");
        WriteBackups(size, now, 5, 4);
        LibraryBackup.Prune(size, 10, 8, TimeSpan.FromDays(30), now);
        Assert.Equal(8, LibraryBackup.List(size).Sum(b => b.Bytes));

        string age = t.Of("age");
        WriteBackups(age, now, 5, 4);
        LibraryBackup.Prune(age, 10, 100, TimeSpan.FromDays(2), now);
        Assert.Equal(3, LibraryBackup.List(age).Count);

        string allOld = t.Of("all-old");
        WriteBackups(allOld, now.AddDays(-100), 3, 4);
        LibraryBackup.Prune(allOld, 10, 100, TimeSpan.FromDays(2), now);
        Assert.Single(LibraryBackup.List(allOld));
    }

    [Fact]
    public void Restore_OldestRetainedBackup_SurvivesPreRestorePruning()
    {
        using var t = new TempDir();
        var lib = t.Of("lib");
        var backups = t.Of("backups");
        var config = t.Of("config.json");
        TestFiles.WriteResponse(Path.Combine(lib, "Original.json"), html: "<p>oldest</p>");
        File.WriteAllText(config, """{"theme":"Dark"}""");

        for (int i = 0; i < LibraryBackup.MaxKeep; i++)
        {
            var path = LibraryBackup.Create(lib, backups, config, null)!;
            File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-LibraryBackup.MaxKeep + i));
        }
        var oldest = LibraryBackup.List(backups).Last();

        Directory.Delete(lib, recursive: true);
        TestFiles.WriteResponse(Path.Combine(lib, "Changed.json"), html: "<p>current</p>");
        LibraryBackup.Restore(oldest.Path, lib, backups, config, null);

        Assert.True(File.Exists(Path.Combine(lib, "Original.json")));
        Assert.False(File.Exists(Path.Combine(lib, "Changed.json")));
    }

    [Fact]
    public void Restore_NewSnapshot_RestoresConfigAndLibrary()
    {
        using var t = new TempDir();
        var lib = t.Of("lib");
        var backups = t.Of("backups");
        var config = t.Of("config.json");
        TestFiles.WriteResponse(Path.Combine(lib, "Original.json"), html: "<p>v1</p>");
        File.WriteAllText(config, """{"theme":"Dark","hotkeyGesture":"Ctrl+Alt+D","libraryPath":"C:\\OldLibrary"}""");
        var snapshot = LibraryBackup.Create(lib, backups, config, null)!;

        Directory.Delete(lib, recursive: true);
        TestFiles.WriteResponse(Path.Combine(lib, "Changed.json"), html: "<p>v2</p>");
        File.WriteAllText(config, """{"theme":"Light"}""");
        bool restoredConfig = LibraryBackup.Restore(snapshot, lib, backups, config, null);

        Assert.True(File.Exists(Path.Combine(lib, "Original.json")));
        Assert.False(File.Exists(Path.Combine(lib, "Changed.json")));
        Assert.True(restoredConfig);
        var restored = QuickReplyKit.Core.Storage.AppConfig.Load(config);
        Assert.Equal(QuickReplyKit.Core.Storage.AppTheme.Dark, restored.Theme);
        Assert.Equal("Ctrl+Alt+D", restored.HotkeyGesture);
        Assert.Equal(Path.GetFullPath(lib), restored.LibraryPath);
    }

    [Fact]
    public void Restore_LegacyLibraryOnlyZip_LeavesConfigUnchanged()
    {
        using var t = new TempDir();
        var legacySource = t.Of("legacy");
        TestFiles.WriteResponse(Path.Combine(legacySource, "Nested", "Old.json"), html: "<p>legacy</p>");
        var snapshot = t.Of("qrk-backup-legacy.zip");
        ZipFile.CreateFromDirectory(legacySource, snapshot);

        var lib = t.Of("live");
        var backups = t.Of("backups");
        var config = t.Of("config.json");
        TestFiles.WriteResponse(Path.Combine(lib, "Current.json"));
        File.WriteAllText(config, "keep this config");

        bool restoredConfig = LibraryBackup.Restore(snapshot, lib, backups, config, null);

        Assert.True(File.Exists(Path.Combine(lib, "Nested", "Old.json")));
        Assert.False(File.Exists(Path.Combine(lib, "Current.json")));
        Assert.False(restoredConfig);
        Assert.Equal("keep this config", File.ReadAllText(config));
    }

    [Fact]
    public void Restore_ForcedPartialInstallFailure_RollsBackExactOriginal()
    {
        using var t = new TempDir();
        var lib = t.Of("lib");
        var backups = t.Of("backups");
        var config = t.Of("config.json");

        TestFiles.WriteResponse(Path.Combine(lib, "FromBackup.json"), html: "<p>backup</p>");
        File.WriteAllText(config, """{"theme":"Dark"}""");
        var snapshot = LibraryBackup.Create(lib, backups, config, null)!;

        Directory.Delete(lib, recursive: true);
        TestFiles.WriteResponse(Path.Combine(lib, "Current", "Keep.json"), html: "<p>exact original</p>");
        Directory.CreateDirectory(Path.Combine(lib, "Empty"));
        File.WriteAllText(config, """{"theme":"Light"}""");
        var expected = SnapshotTree(lib);

        var ex = Assert.Throws<IOException>(() => LibraryBackup.Restore(
            snapshot, lib, backups, config,
            (source, destination) =>
            {
                Directory.CreateDirectory(destination);
                File.Copy(Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).First(),
                    Path.Combine(destination, "partial.json"));
                throw new IOException("forced install failure");
            }));

        Assert.Contains("original library was restored", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expected, SnapshotTree(lib));
        Assert.Equal("""{"theme":"Light"}""", File.ReadAllText(config));
    }

    [Fact]
    public void Restore_UnsafeOrMalformedSnapshot_DoesNotTouchLiveLibrary()
    {
        using var t = new TempDir();
        var zipPath = t.Of("bad.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("quickreplykit-backup.json").Open()))
                writer.Write("""{"Schema":2,"ConfigIncluded":false}""");
            using (var writer = new StreamWriter(zip.CreateEntry("library/../../escape.json").Open()))
                writer.Write("{}");
        }

        var lib = t.Of("lib");
        TestFiles.WriteResponse(Path.Combine(lib, "Keep.json"), html: "<p>untouched</p>");
        var expected = SnapshotTree(lib);

        Assert.Throws<InvalidDataException>(() =>
            LibraryBackup.Restore(zipPath, lib, t.Of("backups"), t.Of("config.json"), null));
        Assert.Equal(expected, SnapshotTree(lib));
        Assert.False(File.Exists(t.Of("escape.json")));
    }

    [Fact]
    public void Restore_RejectsArchiveExpansionLimits_BeforeTouchingLiveLibrary()
    {
        using var t = new TempDir();
        var zipPath = t.Of("large.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("a.json").Open())) writer.Write("1234");
            using (var writer = new StreamWriter(zip.CreateEntry("b.json").Open())) writer.Write("5678");
        }
        var lib = t.Of("lib");
        TestFiles.WriteResponse(Path.Combine(lib, "Keep.json"));
        string expected = SnapshotTree(lib);

        Assert.Throws<InvalidDataException>(() => LibraryBackup.Restore(
            zipPath, lib, t.Of("backups"), t.Of("config.json"), null,
            maxEntries: 1, maxEntryBytes: 10, maxExpandedBytes: 20));
        Assert.Throws<InvalidDataException>(() => LibraryBackup.Restore(
            zipPath, lib, t.Of("backups"), t.Of("config.json"), null,
            maxEntries: 10, maxEntryBytes: 3, maxExpandedBytes: 20));
        Assert.Throws<InvalidDataException>(() => LibraryBackup.Restore(
            zipPath, lib, t.Of("backups"), t.Of("config.json"), null,
            maxEntries: 10, maxEntryBytes: 10, maxExpandedBytes: 7));

        Assert.Equal(expected, SnapshotTree(lib));
    }

    [Fact]
    public void CreateRequired_UnwritableDestination_FailsClosedWithClearMessage()
    {
        using var t = new TempDir();
        var lib = t.Of("lib");
        TestFiles.WriteResponse(Path.Combine(lib, "Keep.json"), html: "<p>still here</p>");
        var expected = SnapshotTree(lib);
        var blocked = t.Of("not-a-folder");
        File.WriteAllText(blocked, "file blocks directory creation");

        var ex = Assert.Throws<IOException>(() =>
            LibraryBackup.CreateRequired(lib, blocked, t.Of("config.json"), "before import"));

        Assert.Contains("No changes were made", ex.Message);
        Assert.Equal(expected, SnapshotTree(lib));
    }

    [Fact]
    public void Restore_MissingBackup_Throws()
    {
        using var t = new TempDir();
        Assert.Throws<FileNotFoundException>(() =>
            LibraryBackup.Restore(t.Of("nope.zip"), t.Of("lib"), t.Of("backups")));
    }

    private static void WriteBackups(string directory, DateTime now, int count, int bytes)
    {
        Directory.CreateDirectory(directory);
        for (int i = 0; i < count; i++)
        {
            string path = Path.Combine(directory, $"qrk-backup-{i}.zip");
            File.WriteAllBytes(path, Enumerable.Repeat((byte)i, bytes).ToArray());
            File.SetLastWriteTime(path, now.AddDays(-i));
        }
    }

    private static string SnapshotTree(string root)
    {
        var dirs = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Select(p => "D:" + Path.GetRelativePath(root, p).Replace('\\', '/'));
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(p => "F:" + Path.GetRelativePath(root, p).Replace('\\', '/') + ":" +
                Convert.ToBase64String(File.ReadAllBytes(p)));
        return string.Join("\n", dirs.Concat(files).OrderBy(x => x, StringComparer.Ordinal));
    }
}
