using System.IO;
using System.Text.Json;
using QuickReplyKit.Core.Model;

namespace QuickReplyKit.Core.Storage;

/// <summary>
/// Reads/writes the schema-v1 response file. Loading is tolerant: a malformed file, or one
/// that isn't ours (no schema/html), yields an invalid <see cref="ResponseItem"/> rather than
/// throwing, so one bad file never blocks its siblings.
/// </summary>
public static class ResponseFile
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // schema and html are nullable so we can tell "missing" from a real value.
    private sealed class Dto
    {
        public int? Schema { get; set; }
        public string? App { get; set; }
        public List<string>? Tags { get; set; }
        public string? Html { get; set; }
        public bool Favorite { get; set; }
        public DateTimeOffset? Created { get; set; }
        public DateTimeOffset? Modified { get; set; }
    }

    public static ResponseItem Load(string fullPath, string libraryRoot)
    {
        var item = new ResponseItem
        {
            Name = Path.GetFileNameWithoutExtension(fullPath),
            FullPath = fullPath,
            RelativePath = ToRelative(libraryRoot, fullPath),
            CategoryPath = CategoryOf(libraryRoot, fullPath),
        };

        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(fullPath), ReadOptions);
            if (dto is null || dto.Schema is null || dto.Html is null)
                return Invalidate(item, "Not a QuickReplyKit response (missing schema or html).");

            item.App = dto.App ?? "";
            item.Tags = dto.Tags ?? new List<string>();
            item.Html = dto.Html;
            item.Favorite = dto.Favorite;
            item.Created = dto.Created ?? FileTime(fullPath, creation: true);
            item.Modified = dto.Modified ?? FileTime(fullPath, creation: false);
            item.IsValid = true;
            return item;
        }
        catch (Exception ex)
        {
            return Invalidate(item, ex.Message);
        }
    }

    public static void Save(ResponseItem item, string libraryRoot)
    {
        item.Modified = DateTimeOffset.UtcNow;
        if (item.Created == default) item.Created = item.Modified;

        string rel = string.IsNullOrEmpty(item.CategoryPath)
            ? item.Name + ".json"
            : item.CategoryPath + "/" + item.Name + ".json";
        string fullPath = Path.Combine(libraryRoot, rel.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var dto = new Dto
        {
            Schema = 1,
            App = item.App,
            Tags = item.Tags.ToList(),
            Html = item.Html,
            Favorite = item.Favorite,
            Created = item.Created,
            Modified = item.Modified,
        };
        File.WriteAllText(fullPath, JsonSerializer.Serialize(dto, WriteOptions));

        item.FullPath = fullPath;
        item.RelativePath = ToRelative(libraryRoot, fullPath);
    }

    private static ResponseItem Invalidate(ResponseItem item, string error)
    {
        item.IsValid = false;
        item.Error = error;
        item.Created = FileTime(item.FullPath, creation: true);
        item.Modified = FileTime(item.FullPath, creation: false);
        return item;
    }

    private static DateTimeOffset FileTime(string path, bool creation)
    {
        try
        {
            return creation ? File.GetCreationTimeUtc(path) : File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTimeOffset.UtcNow;
        }
    }

    private static string ToRelative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static string CategoryOf(string root, string fullPath)
    {
        string? dir = Path.GetDirectoryName(fullPath);
        if (dir is null) return "";
        string rel = Path.GetRelativePath(root, dir);
        return rel == "." ? "" : rel.Replace('\\', '/');
    }
}
