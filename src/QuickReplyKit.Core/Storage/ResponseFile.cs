using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickReplyKit.Core.Model;

namespace QuickReplyKit.Core.Storage;

/// <summary>
/// Reads/writes the schema-v1 response file. Loading is tolerant per field: a malformed file, or
/// one that isn't ours (no schema/html), yields an invalid <see cref="ResponseItem"/> rather than
/// throwing, so one bad file never blocks its siblings. A single odd or wrong-typed field (e.g.
/// <c>tags</c> as a string, <c>favorite</c> as text) is recovered to a sensible default and does
/// not invalidate a response whose essential <c>html</c> is usable.
/// </summary>
public static class ResponseFile
{
    private static readonly JsonNodeOptions NodeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Be forgiving of hand edits: tolerate trailing commas and // comments.
    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
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
            var root = JsonNode.Parse(File.ReadAllText(fullPath), NodeOptions, DocOptions);
            if (root is not JsonObject obj)
                return Invalidate(item, "Not a QuickReplyKit response (not a JSON object).");

            // Essential markers: our schema id (any value marks the file as ours) and a usable
            // html string. Everything else is read defensively and falls back on defaults.
            string? html = AsString(obj["html"]);
            if (obj["schema"] is null || html is null)
                return Invalidate(item, "Not a QuickReplyKit response (missing schema or html).");

            item.Html = html;
            item.App = AsString(obj["app"]) ?? "";
            item.Tags = ReadTags(obj["tags"]);
            item.Favorite = AsBool(obj["favorite"]) ?? false;
            item.Created = AsDate(obj["created"]) ?? FileTime(fullPath, creation: true);
            item.Modified = AsDate(obj["modified"]) ?? FileTime(fullPath, creation: false);
            item.IsValid = true;
            return item;
        }
        catch (Exception ex)
        {
            return Invalidate(item, ex.Message);
        }
    }

    private static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static bool? AsBool(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out bool b) ? b : null;

    private static DateTimeOffset? AsDate(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue(out DateTimeOffset dto)) return dto;
        return v.TryGetValue(out string? s) && DateTimeOffset.TryParse(s, out var parsed) ? parsed : null;
    }

    // Array of strings (skipping non-string elements); a bare string counts as one tag; anything
    // else → no tags.
    private static IReadOnlyList<string> ReadTags(JsonNode? node)
    {
        if (node is JsonArray arr)
        {
            var list = new List<string>();
            foreach (var el in arr)
                if (AsString(el) is string s) list.Add(s);
            return list;
        }
        return AsString(node) is string single ? new List<string> { single } : new List<string>();
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
