using System.IO;
using System.Text.Json;

namespace QuickReplyKit.Core.Storage;

public sealed class RecentEntry
{
    public string RelativePath { get; set; } = "";
    public DateTimeOffset UsedAt { get; set; }
}

/// <summary>
/// Most-recent-first list of used responses (relative paths + timestamps), capped and
/// de-duplicated. Local-only, stored at <see cref="AppPaths.RecentPath"/>; tolerant of a
/// missing or corrupt file.
/// </summary>
public sealed class RecentStore
{
    public const int DefaultCap = 25;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly List<RecentEntry> _items = new();

    public int Cap { get; }

    public RecentStore(int cap = DefaultCap) => Cap = cap;

    public IReadOnlyList<(string RelativePath, DateTimeOffset UsedAt)> Items =>
        _items.Select(e => (e.RelativePath, e.UsedAt)).ToList();

    /// <summary>Records a use: moves the path to the front, trims to <see cref="Cap"/>.</summary>
    public void Add(string relativePath)
    {
        _items.RemoveAll(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, new RecentEntry { RelativePath = relativePath, UsedAt = DateTimeOffset.UtcNow });
        if (_items.Count > Cap) _items.RemoveRange(Cap, _items.Count - Cap);
    }

    public static RecentStore Load(int cap = DefaultCap) => Load(AppPaths.RecentPath, cap);

    internal static RecentStore Load(string path, int cap = DefaultCap)
    {
        var store = new RecentStore(cap);
        try
        {
            if (File.Exists(path))
            {
                var entries = JsonSerializer.Deserialize<List<RecentEntry>>(File.ReadAllText(path), Options);
                if (entries is not null)
                {
                    foreach (var e in entries.Where(e => !string.IsNullOrEmpty(e.RelativePath)).Take(cap))
                        store._items.Add(e);
                }
            }
        }
        catch { /* corrupt file → empty store */ }
        return store;
    }

    public void Save()
    {
        AppPaths.EnsureAppData();
        Save(AppPaths.RecentPath);
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(_items, Options));
    }
}
