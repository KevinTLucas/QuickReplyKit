using System.IO;
using System.Text.Json;

namespace QuickReplyKit.Core.Storage;

/// <summary>One soft-deleted response sitting in the app-local trash.</summary>
public sealed record TrashedItem(string Id, string Name, string OriginalRelativePath, DateTime DeletedUtc, long Bytes);

/// <summary>
/// App-local trash for responses: a soft delete that keeps the file for a retention window with
/// first-class Restore / Empty, so "I deleted our whole IXL folder" is an in-product undo rather than
/// a support ticket. Each trashed response lives in its own <c>trash/&lt;id&gt;/</c> folder alongside a
/// <c>meta.json</c> recording where it came from; Restore puts it back at its original library path.
/// (This replaces the Recycle-Bin delete for responses — the launcher's Delete now moves here.)
/// </summary>
public static class TrashService
{
    private const string MetaName = "meta.json";

    private sealed class Meta
    {
        public string Name { get; set; } = "";
        public string OriginalRelativePath { get; set; } = "";
        public DateTime DeletedUtc { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>Move a response file into the trash; returns the new trash id.</summary>
    public static string Trash(string responseFullPath, string libraryRoot, string trashDir)
    {
        string rel = Path.GetRelativePath(libraryRoot, responseFullPath).Replace('\\', '/');
        string id = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
        string dir = Path.Combine(trashDir, id);
        Directory.CreateDirectory(dir);

        File.Move(responseFullPath, Path.Combine(dir, Path.GetFileName(responseFullPath)), overwrite: true);
        var meta = new Meta
        {
            Name = Path.GetFileNameWithoutExtension(responseFullPath),
            OriginalRelativePath = rel,
            DeletedUtc = DateTime.UtcNow,
        };
        File.WriteAllText(Path.Combine(dir, MetaName), JsonSerializer.Serialize(meta, Options));
        return id;
    }

    /// <summary>Trashed items, newest first. Malformed entries are skipped, never fatal.</summary>
    public static IReadOnlyList<TrashedItem> List(string trashDir)
    {
        if (!Directory.Exists(trashDir)) return System.Array.Empty<TrashedItem>();
        var items = new List<TrashedItem>();
        foreach (var dir in Directory.EnumerateDirectories(trashDir))
        {
            var meta = ReadMeta(dir);
            if (meta is null) continue;
            var file = ResponseFileIn(dir);
            long bytes = file is not null ? new FileInfo(file).Length : 0;
            items.Add(new TrashedItem(Path.GetFileName(dir), meta.Name, meta.OriginalRelativePath, meta.DeletedUtc, bytes));
        }
        items.Sort((a, b) => b.DeletedUtc.CompareTo(a.DeletedUtc));
        return items;
    }

    /// <summary>
    /// Restore a trashed response to its original library path. Returns false (leaving the item in the
    /// trash) if something already exists at that path, so the caller can resolve the conflict.
    /// </summary>
    public static bool Restore(string id, string libraryRoot, string trashDir)
    {
        string dir = Path.Combine(trashDir, id);
        var meta = ReadMeta(dir);
        var file = ResponseFileIn(dir);
        if (meta is null || file is null) return false;

        string target = Path.GetFullPath(Path.Combine(libraryRoot, meta.OriginalRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        // Defence in depth: a hand-edited meta.json must never let a restore escape the library root.
        string root = Path.GetFullPath(libraryRoot);
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase)) return false;
        if (File.Exists(target)) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(file, target);
        Directory.Delete(dir, recursive: true);
        return true;
    }

    /// <summary>Permanently remove one trashed item.</summary>
    public static void DeleteForever(string id, string trashDir)
    {
        string dir = Path.Combine(trashDir, id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>Permanently remove everything in the trash.</summary>
    public static void Empty(string trashDir)
    {
        if (!Directory.Exists(trashDir)) return;
        foreach (var dir in Directory.EnumerateDirectories(trashDir))
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Auto-purge items older than <paramref name="retention"/>; returns how many were removed.</summary>
    public static int Purge(string trashDir, TimeSpan retention)
    {
        int n = 0;
        var cutoff = DateTime.UtcNow - retention;
        foreach (var item in List(trashDir))
            if (item.DeletedUtc < cutoff) { DeleteForever(item.Id, trashDir); n++; }
        return n;
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

    private static string? ResponseFileIn(string dir) =>
        Directory.EnumerateFiles(dir, "*.json").FirstOrDefault(f =>
            !string.Equals(Path.GetFileName(f), MetaName, System.StringComparison.OrdinalIgnoreCase));
}
