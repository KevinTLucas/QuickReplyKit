using System.IO;
using System.Text.Json;
using QuickReplyKit.Core.ImportExport;
using FileSystem = Microsoft.VisualBasic.FileIO.FileSystem;

namespace QuickReplyKit.Core.Storage;

public enum TrashItemKind { Response, Folder }
public enum TrashRestoreResult { Restored, Conflict, Invalid }

/// <summary>One soft-deleted response or folder sitting in the app-local trash.</summary>
public sealed record TrashedItem(string Id, string Name, string OriginalRelativePath, DateTime DeletedUtc,
    long Bytes, TrashItemKind Kind = TrashItemKind.Response);

/// <summary>
/// App-local soft delete. Each item lives in its own <c>trash/&lt;id&gt;/</c> directory beside metadata
/// recording its original library-relative path. Folder deletion requires a safety backup first.
/// </summary>
public static class TrashService
{
    private const string MetaName = "meta.json";
    private const string FolderPayload = "payload";

    private sealed class Meta
    {
        public string Name { get; set; } = "";
        public string OriginalRelativePath { get; set; } = "";
        public DateTime DeletedUtc { get; set; }
        public string? Kind { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>Move one response into Trash. Existing file-only metadata remains compatible.</summary>
    public static string Trash(string responseFullPath, string libraryRoot, string trashDir)
    {
        string source = RequireLibraryItem(responseFullPath, libraryRoot, folder: false);
        return MoveToTrash(source, libraryRoot, trashDir, TrashItemKind.Response);
    }

    /// <summary>Back up the library, then move one category subtree into Trash.</summary>
    public static string TrashFolder(string folderFullPath, string libraryRoot, string trashDir, string backupsDir)
    {
        string source = RequireLibraryItem(folderFullPath, libraryRoot, folder: true);
        if (ContainsReparsePoint(libraryRoot))
            throw new IOException("The library contains a link or junction and the folder was not deleted.");

        LibraryBackup.CreateRequired(libraryRoot, backupsDir, "before deleting folder");

        // The tree could have changed while the backup was written. Fail closed before moving it.
        source = RequireLibraryItem(source, libraryRoot, folder: true);
        if (ContainsReparsePoint(source))
            throw new IOException("The folder contains a link or junction and was not deleted.");
        return MoveToTrash(source, libraryRoot, trashDir, TrashItemKind.Folder);
    }

    /// <summary>Trashed items, newest first. Malformed or linked entries are skipped, never fatal.</summary>
    public static IReadOnlyList<TrashedItem> List(string trashDir)
    {
        if (!SafeRoot(trashDir, create: false)) return Array.Empty<TrashedItem>();
        var items = new List<TrashedItem>();
        string[] directories;
        try { directories = Directory.GetDirectories(trashDir); }
        catch { return items; }
        foreach (var dir in directories)
        {
            if (IsReparsePoint(dir)) continue;
            var meta = ReadMeta(dir);
            if (meta is null || !TryKind(meta, out var kind)) continue;
            string? payload = PayloadIn(dir, kind);
            if (payload is null || ContainsReparsePoint(payload)) continue;
            items.Add(new TrashedItem(Path.GetFileName(dir), meta.Name, meta.OriginalRelativePath,
                meta.DeletedUtc, SizeOf(payload), kind));
        }
        items.Sort((a, b) => b.DeletedUtc.CompareTo(a.DeletedUtc));
        return items;
    }

    /// <summary>Compatibility wrapper: true only when the item was restored.</summary>
    public static bool Restore(string id, string libraryRoot, string trashDir) =>
        RestoreDetailed(id, libraryRoot, trashDir) == TrashRestoreResult.Restored;

    /// <summary>Restore without overwrite; malformed, linked, or escaping metadata fails closed.</summary>
    public static TrashRestoreResult RestoreDetailed(string id, string libraryRoot, string trashDir)
    {
        if (!Directory.Exists(libraryRoot) || IsReparsePoint(libraryRoot) ||
            !TryEntryDirectory(trashDir, id, out string dir)) return TrashRestoreResult.Invalid;

        var meta = ReadMeta(dir);
        if (meta is null || !TryKind(meta, out var kind)) return TrashRestoreResult.Invalid;
        string? payload = PayloadIn(dir, kind);
        if (payload is null || ContainsReparsePoint(payload) ||
            !TryLibraryTarget(libraryRoot, meta.OriginalRelativePath, out string target))
            return TrashRestoreResult.Invalid;

        if (File.Exists(target) || Directory.Exists(target)) return TrashRestoreResult.Conflict;
        string parent = Path.GetDirectoryName(target)!;
        if (HasReparsePoint(libraryRoot, parent)) return TrashRestoreResult.Invalid;

        if (HasFileInPath(libraryRoot, parent)) return TrashRestoreResult.Conflict;
        try
        {
            Directory.CreateDirectory(parent);
            if (HasReparsePoint(libraryRoot, parent)) return TrashRestoreResult.Invalid;
            if (kind == TrashItemKind.Folder) FileSystem.MoveDirectory(payload, target);
            else FileSystem.MoveFile(payload, target);
        }
        catch
        {
            return File.Exists(target) || Directory.Exists(target)
                ? TrashRestoreResult.Conflict : TrashRestoreResult.Invalid;
        }
        try { Directory.Delete(dir, recursive: true); } catch { /* payload already restored */ }
        return TrashRestoreResult.Restored;
    }

    /// <summary>Permanently remove one safe Trash entry.</summary>
    public static bool DeleteForever(string id, string trashDir)
    {
        if (!TryEntryDirectory(trashDir, id, out string dir) || ContainsReparsePoint(dir)) return false;
        try { Directory.Delete(dir, recursive: true); return true; }
        catch { return false; }
    }

    /// <summary>Permanently remove every safe item in Trash.</summary>
    public static void Empty(string trashDir)
    {
        foreach (var item in List(trashDir))
            try { DeleteForever(item.Id, trashDir); } catch { /* best effort */ }
    }

    /// <summary>Auto-purge items older than <paramref name="retention"/>; returns how many were removed.</summary>
    public static int Purge(string trashDir, TimeSpan retention)
    {
        int n = 0;
        var cutoff = DateTime.UtcNow - retention;
        foreach (var item in List(trashDir))
            if (item.DeletedUtc < cutoff && DeleteForever(item.Id, trashDir)) n++;
        return n;
    }

    private static string MoveToTrash(string source, string libraryRoot, string trashDir, TrashItemKind kind)
    {
        if (!SafeRoot(trashDir, create: true)) throw new IOException("The Trash folder is unavailable or unsafe.");
        string id = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
        string dir = Path.Combine(Path.GetFullPath(trashDir), id);
        Directory.CreateDirectory(dir);
        var meta = new Meta
        {
            Name = kind == TrashItemKind.Folder ? Path.GetFileName(source) : Path.GetFileNameWithoutExtension(source),
            OriginalRelativePath = Path.GetRelativePath(libraryRoot, source).Replace('\\', '/'),
            DeletedUtc = DateTime.UtcNow,
            Kind = kind == TrashItemKind.Folder ? "folder" : "response",
        };

        try
        {
            File.WriteAllText(Path.Combine(dir, MetaName), JsonSerializer.Serialize(meta, Options));
            if (kind == TrashItemKind.Folder) FileSystem.MoveDirectory(source, Path.Combine(dir, FolderPayload));
            else FileSystem.MoveFile(source, Path.Combine(dir, Path.GetFileName(source)));
            return id;
        }
        catch
        {
            try
            {
                if (Directory.Exists(dir) &&
                    !Directory.EnumerateFileSystemEntries(dir).Any(p => p != Path.Combine(dir, MetaName)))
                    Directory.Delete(dir, true);
            }
            catch { /* keep any recoverable payload */ }
            throw;
        }
    }

    private static string RequireLibraryItem(string path, string libraryRoot, bool folder)
    {
        string root = Path.GetFullPath(libraryRoot);
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(root) || IsReparsePoint(root) || !IsStrictlyInside(root, full) ||
            HasReparsePoint(root, full) || (folder ? !Directory.Exists(full) : !File.Exists(full)))
            throw new IOException(folder ? "The folder is missing or unsafe and was not deleted."
                                         : "The response is missing or unsafe and was not deleted.");
        return full;
    }

    private static bool TryLibraryTarget(string libraryRoot, string relative, out string target)
    {
        target = "";
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return false;
        try
        {
            string root = Path.GetFullPath(libraryRoot);
            target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            return IsStrictlyInside(root, target);
        }
        catch { return false; }
    }

    private static bool TryEntryDirectory(string trashDir, string id, out string dir)
    {
        dir = "";
        if (string.IsNullOrWhiteSpace(id) || id != Path.GetFileName(id) || !SafeRoot(trashDir, create: false)) return false;
        try
        {
            string root = Path.GetFullPath(trashDir);
            dir = Path.GetFullPath(Path.Combine(root, id));
            return IsStrictlyInside(root, dir) && Directory.Exists(dir) && !IsReparsePoint(dir);
        }
        catch { return false; }
    }

    private static bool SafeRoot(string path, bool create)
    {
        try
        {
            if (create) Directory.CreateDirectory(path);
            return Directory.Exists(path) && !IsReparsePoint(path);
        }
        catch { return false; }
    }

    private static bool IsStrictlyInside(string root, string full)
    {
        string rel = Path.GetRelativePath(root, full);
        return rel != "." && rel != ".." && !Path.IsPathRooted(rel) &&
            !rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool HasReparsePoint(string root, string path)
    {
        if (IsReparsePoint(root)) return true;
        string current = root;
        foreach (string part in Path.GetRelativePath(root, path)
                     .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((Directory.Exists(current) || File.Exists(current)) && IsReparsePoint(current)) return true;
        }
        return false;
    }

    private static bool HasFileInPath(string root, string path)
    {
        string current = root;
        foreach (string part in Path.GetRelativePath(root, path)
                     .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if (File.Exists(current)) return true;
        }
        return false;
    }

    private static bool ContainsReparsePoint(string path)
    {
        try
        {
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                if (IsReparsePoint(current)) return true;
                if (!Directory.Exists(current)) continue;
                foreach (string child in Directory.EnumerateFileSystemEntries(current)) pending.Push(child);
            }
            return false;
        }
        catch { return true; }
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }

    private static Meta? ReadMeta(string dir)
    {
        try
        {
            string path = Path.Combine(dir, MetaName);
            return File.Exists(path) ? JsonSerializer.Deserialize<Meta>(File.ReadAllText(path), Options) : null;
        }
        catch { return null; }
    }

    private static bool TryKind(Meta meta, out TrashItemKind kind)
    {
        kind = TrashItemKind.Response;
        if (string.IsNullOrEmpty(meta.Kind) || meta.Kind.Equals("response", StringComparison.OrdinalIgnoreCase)) return true;
        if (meta.Kind.Equals("folder", StringComparison.OrdinalIgnoreCase)) { kind = TrashItemKind.Folder; return true; }
        return false;
    }

    private static string? PayloadIn(string dir, TrashItemKind kind)
    {
        try
        {
            return kind == TrashItemKind.Folder
                ? Directory.Exists(Path.Combine(dir, FolderPayload)) ? Path.Combine(dir, FolderPayload) : null
                : Directory.EnumerateFiles(dir, "*.json").FirstOrDefault(f =>
                    !Path.GetFileName(f).Equals(MetaName, StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    private static long SizeOf(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            long total = 0;
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                total = checked(total + new FileInfo(file).Length);
            return total;
        }
        catch { return 0; }
    }
}
