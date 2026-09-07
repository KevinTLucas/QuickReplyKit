using System.IO;
using System.IO.Compression;

namespace QuickReplyKit.ViewModels;

public enum ImportKind { New, Identical, Conflict }

/// <summary>One planned import entry and what would happen to it.</summary>
public sealed record ImportEntry(string Dest, ImportKind Kind);

/// <summary>
/// Read-only pre-import diff: classify each incoming .json against the current library by destination
/// path + content, so the user sees what is <b>New</b>, byte-<b>Identical</b> (a no-op), or a
/// <b>Conflict</b> (same path, different content → needs a Skip/Overwrite/Keep-both decision) BEFORE
/// anything is written. Nothing here mutates the library — the actual copy stays in <c>LibraryPorter</c>.
/// </summary>
public static class ImportPreview
{
    public static List<ImportEntry> Analyze(string libraryRoot, string sourcePath, string targetCategory)
    {
        var list = new List<ImportEntry>();
        foreach (var (rel, content) in SourceEntries(sourcePath))
        {
            string relDest = ImportCoordinator.TargetPath(targetCategory, rel);
            string dest = Path.Combine(libraryRoot, relDest.Replace('/', Path.DirectorySeparatorChar));
            ImportKind kind =
                !File.Exists(dest) ? ImportKind.New
                : SameBytes(content, File.ReadAllBytes(dest)) ? ImportKind.Identical
                : ImportKind.Conflict;
            list.Add(new ImportEntry(relDest, kind));
        }
        return list;
    }

    public static int Count(IEnumerable<ImportEntry> entries, ImportKind kind) => entries.Count(e => e.Kind == kind);

    private static bool SameBytes(byte[] a, byte[] b) => a.Length == b.Length && a.AsSpan().SequenceEqual(b);

    /// <summary>Each incoming .json entry as (relative path, bytes): a single file, or every zip entry.</summary>
    private static IEnumerable<(string rel, byte[] content)> SourceEntries(string sourcePath)
    {
        if (!sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            yield return (Path.GetFileName(sourcePath), File.ReadAllBytes(sourcePath));
            yield break;
        }
        using var zip = ZipFile.OpenRead(sourcePath);
        foreach (var e in zip.Entries)
        {
            if (e.Name.Length == 0 || !e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            yield return (e.FullName, ms.ToArray());
        }
    }
}
