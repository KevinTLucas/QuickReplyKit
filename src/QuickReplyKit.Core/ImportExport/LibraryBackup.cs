using System.IO;
using System.IO.Compression;
using QuickReplyKit.Core.Diagnostics;

namespace QuickReplyKit.Core.ImportExport;

/// <summary>One saved library snapshot.</summary>
public sealed record BackupInfo(string Path, DateTime Created, long Bytes);

/// <summary>
/// Rolling library snapshots for one-click undo before risky/bulk operations (import, batch tag,
/// bulk move/delete). Each snapshot is a zip of the whole library under
/// <c>%APPDATA%\QuickReplyKit\backups\</c>; the most recent <see cref="MaxKeep"/> are kept, older ones
/// pruned. Only the response library is snapshotted — config regenerates trivially; the library is the
/// irreplaceable data.
/// </summary>
public static class LibraryBackup
{
    public const int MaxKeep = 10;
    private const string Prefix = "qrk-backup-";

    /// <summary>
    /// Snapshot the library into <paramref name="backupsDir"/> and prune to <see cref="MaxKeep"/>.
    /// Returns the zip path, or <c>null</c> if the library is missing/empty (nothing to back up).
    /// </summary>
    public static string? Create(string libraryRoot, string backupsDir, string? reason = null)
    {
        if (!Directory.Exists(libraryRoot) || !Directory.EnumerateFileSystemEntries(libraryRoot).Any())
            return null;

        Directory.CreateDirectory(backupsDir);
        string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string dest = System.IO.Path.Combine(backupsDir, $"{Prefix}{Stamp()}.zip");
        for (int n = 2; File.Exists(dest); n++)                       // same-second collisions
            dest = System.IO.Path.Combine(backupsDir, $"{Prefix}{Stamp()}-{n}.zip");

        LibraryPorter.ExportLibrary(libraryRoot, dest);
        Prune(backupsDir);
        Diag.Info($"backup created: {System.IO.Path.GetFileName(dest)}" + (reason is null ? "" : $" ({reason})"));
        return dest;
    }

    /// <summary>Saved snapshots, newest first.</summary>
    public static IReadOnlyList<BackupInfo> List(string backupsDir)
    {
        if (!Directory.Exists(backupsDir)) return Array.Empty<BackupInfo>();
        return Directory.EnumerateFiles(backupsDir, Prefix + "*.zip")
            .Select(p => { var fi = new FileInfo(p); return new BackupInfo(p, fi.LastWriteTime, fi.Length); })
            .OrderByDescending(b => b.Created).ToList();
    }

    /// <summary>
    /// Restore a snapshot over the library. First snapshots the CURRENT library (so a restore is itself
    /// undoable), then extracts the backup to a temp folder and, only if that succeeds, replaces the
    /// library contents — a bad/corrupt zip can never leave a half-wiped library. Throws if the backup
    /// is missing.
    /// </summary>
    public static void Restore(string backupZip, string libraryRoot, string backupsDir)
    {
        if (!File.Exists(backupZip)) throw new FileNotFoundException("Backup not found", backupZip);
        Create(libraryRoot, backupsDir, "pre-restore safety");

        string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qrk-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(backupZip, temp);   // .NET rejects path-traversal entries
            Directory.CreateDirectory(libraryRoot);
            foreach (var d in Directory.EnumerateDirectories(libraryRoot)) Directory.Delete(d, recursive: true);
            foreach (var f in Directory.EnumerateFiles(libraryRoot)) File.Delete(f);
            CopyInto(temp, libraryRoot);
            Diag.Info($"library restored from {System.IO.Path.GetFileName(backupZip)}");
        }
        finally { try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch { /* temp */ } }
    }

    private static void Prune(string backupsDir)
    {
        foreach (var b in List(backupsDir).Skip(MaxKeep))
            try { File.Delete(b.Path); } catch { /* best effort */ }
    }

    private static void CopyInto(string src, string dst)
    {
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(System.IO.Path.Combine(dst, System.IO.Path.GetRelativePath(src, dir)));
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, System.IO.Path.Combine(dst, System.IO.Path.GetRelativePath(src, file)), overwrite: true);
    }
}
