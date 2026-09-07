using System.IO;
using System.Text.Json;
using QuickReplyKit.Core.Model;

namespace QuickReplyKit.Core.Storage;

/// <summary>
/// Per-folder metadata stored in a <c>.qrkfolder.json</c> sidecar inside the folder, so folder tags
/// travel when the folder is copied/shared (matching the app's "share by copying folders" model).
/// The library scanner skips this file (see <see cref="FileName"/>), so it never shows as a response;
/// a folder without the sidecar simply has no tags (back-compatible).
/// </summary>
public static class FolderMeta
{
    public const string FileName = ".qrkfolder.json";

    private sealed class Model { public string[] Tags { get; set; } = System.Array.Empty<string>(); }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static bool IsSidecar(string path) =>
        string.Equals(Path.GetFileName(path), FileName, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Tags for a folder, or empty if there's no (or an unreadable) sidecar.</summary>
    public static IReadOnlyList<string> Load(string folderPath)
    {
        try
        {
            string file = Path.Combine(folderPath, FileName);
            if (!File.Exists(file)) return System.Array.Empty<string>();
            var m = JsonSerializer.Deserialize<Model>(File.ReadAllText(file), Options);
            return m?.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? System.Array.Empty<string>();
        }
        catch { return System.Array.Empty<string>(); }
    }

    /// <summary>Write (or, for an empty list, remove) the folder's tag sidecar.</summary>
    public static void Save(string folderPath, IReadOnlyList<string> tags)
    {
        string file = Path.Combine(folderPath, FileName);
        var clean = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(System.StringComparer.OrdinalIgnoreCase).ToArray();
        if (clean.Length == 0)
        {
            if (File.Exists(file)) File.Delete(file);
            return;
        }
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(file, JsonSerializer.Serialize(new Model { Tags = clean }, Options));
    }

    /// <summary>
    /// Union <paramref name="tags"/> into each valid response's tags (deduped, case-insensitive,
    /// existing order preserved) and rewrite only the files that actually gained a tag. Returns how
    /// many responses changed — the caller confirms with that count before/after.
    /// </summary>
    public static int ApplyTags(IEnumerable<ResponseItem> responses, IReadOnlyList<string> tags, string libraryRoot)
    {
        var add = tags.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        if (add.Count == 0) return 0;

        int changed = 0;
        foreach (var r in responses)
        {
            if (!r.IsValid) continue;
            var merged = new List<string>(r.Tags);
            foreach (var t in add)
                if (!merged.Contains(t, System.StringComparer.OrdinalIgnoreCase)) merged.Add(t);
            if (merged.Count == r.Tags.Count) continue;   // nothing new
            r.Tags = merged;
            ResponseFile.Save(r, libraryRoot);
            changed++;
        }
        return changed;
    }
}
