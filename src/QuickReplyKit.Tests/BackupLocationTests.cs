using System.IO;
using QuickReplyKit.Core.ImportExport;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Tests;

public class BackupLocationTests
{
    [Fact]
    public void EnsureWritable_CreatesAndReturnsAbsoluteFolder()
    {
        using var t = new TempDir();
        string path = t.Of("managed", "backups");

        Assert.Equal(Path.GetFullPath(path), BackupLocation.EnsureWritable(path));
        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.EnumerateFiles(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative\\backups")]
    public void EnsureWritable_RejectsNonAbsolutePath(string path)
    {
        var ex = Assert.Throws<IOException>(() => BackupLocation.EnsureWritable(path));
        Assert.Contains("absolute", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureWritable_RejectsFileAndBackupOperationsFailClearly()
    {
        using var t = new TempDir();
        string blocked = t.Of("blocked");
        File.WriteAllText(blocked, "not a directory");

        Assert.Contains("file", Assert.Throws<IOException>(() =>
            BackupLocation.EnsureWritable(blocked)).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("writable", Assert.Throws<IOException>(() =>
            LibraryBackup.CreateRequired(t.Of("library"), blocked, "test")).Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Throws<IOException>(() => LibraryBackup.List(blocked));
    }

    [Fact]
    public void CustomLocation_IsUsedForCreateListAndRestore()
    {
        using var t = new TempDir();
        string library = t.Of("library");
        string backups = t.Of("managed", "snapshots");
        string config = t.Of("config.json");
        TestFiles.WriteResponse(Path.Combine(library, "Original.json"), html: "<p>original</p>");

        string snapshot = LibraryBackup.Create(library, backups, config, "test")!;
        Assert.Equal(Path.GetFullPath(backups), Path.GetDirectoryName(snapshot));
        Assert.Single(LibraryBackup.List(backups));

        File.Delete(Path.Combine(library, "Original.json"));
        TestFiles.WriteResponse(Path.Combine(library, "Changed.json"), html: "<p>changed</p>");
        LibraryBackup.Restore(snapshot, library, backups, config, null);

        Assert.True(File.Exists(Path.Combine(library, "Original.json")));
        Assert.False(File.Exists(Path.Combine(library, "Changed.json")));
        Assert.True(LibraryBackup.List(backups).Count >= 2); // restore creates its safety snapshot here too
    }
}
