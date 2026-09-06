using System.IO;
using System.IO.Compression;

namespace QuickReplyKit.ViewModels;

/// <summary>
/// UI-side glue for import: builds the library-relative target path and peeks (read-only) for name
/// collisions so the launcher can prompt for a batch <c>ConflictPolicy</c> only when conflicts exist.
/// The actual copy/zip work stays in <c>LibraryPorter</c>.
/// </summary>
public static class ImportCoordinator
{
    /// <summary>Library-relative destination for a source entry under a chosen target category.</summary>
    public static string TargetPath(string targetCategory, string entryRelative) =>
        string.IsNullOrEmpty(targetCategory) ? entryRelative : targetCategory + "/" + entryRelative;

    /// <summary>
    /// How many source entries would land on an existing file (read-only; nothing is written).
    /// Counts by name only — an invalid entry that collides is skipped by the real import anyway,
    /// so at worst we prompt once too often; the final summary always reflects reality.
    /// </summary>
    public static int CountConflicts(string libraryRoot, string sourcePath, string targetCategory)
    {
        int n = 0;
        foreach (var rel in SourceEntries(sourcePath))
        {
            string dest = Path.Combine(libraryRoot,
                TargetPath(targetCategory, rel).Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(dest)) n++;
        }
        return n;
    }

    /// <summary>Relative .json entry paths inside the source (a single file, or every entry of a zip).</summary>
    private static IEnumerable<string> SourceEntries(string sourcePath)
    {
        if (!sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.GetFileName(sourcePath);
            yield break;
        }
        using var zip = ZipFile.OpenRead(sourcePath);
        foreach (var e in zip.Entries)
            if (e.Name.Length > 0 && e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                yield return e.FullName;
    }
}
