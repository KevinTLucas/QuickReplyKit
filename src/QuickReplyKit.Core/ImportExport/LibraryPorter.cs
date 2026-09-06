using System.IO;
using System.IO.Compression;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Core.ImportExport;

/// <summary>What to do when an imported response would land on an existing file.</summary>
public enum ConflictPolicy
{
    /// <summary>Replace the existing file.</summary>
    Overwrite,
    /// <summary>Keep both — write the import under a "(imported)" suffixed name.</summary>
    KeepBoth,
    /// <summary>Leave the existing file untouched and drop the import.</summary>
    Skip,
}

/// <summary>Outcome of an <see cref="LibraryPorter.Import"/> run.</summary>
public sealed class ImportResult
{
    public int Imported { get; set; }
    public int SkippedInvalid { get; set; }
    public int Conflicts { get; set; }
    public List<string> Messages { get; } = new();
}

/// <summary>
/// Headless import/export for the response library. Exports are plain file/zip copies; imports
/// parse-check every entry with <see cref="ResponseFile.Load"/> and process each independently so
/// one broken file never aborts the batch. BCL only (<see cref="System.IO.Compression"/>).
/// </summary>
public static class LibraryPorter
{
    /// <summary>Copy a single response .json to <paramref name="destPath"/>.</summary>
    public static void Export(ResponseItem item, string destPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destPath))!);
        File.Copy(item.FullPath, destPath, overwrite: true);
    }

    /// <summary>Zip a category subtree, entries relative to that folder (hierarchy preserved).</summary>
    public static void ExportFolder(string libraryRoot, string categoryPath, string destZip)
    {
        string source = string.IsNullOrEmpty(categoryPath)
            ? libraryRoot
            : Path.Combine(libraryRoot, categoryPath.Replace('/', Path.DirectorySeparatorChar));
        ZipDirectory(source, destZip);
    }

    /// <summary>Zip the entire library, entries relative to the root.</summary>
    public static void ExportLibrary(string libraryRoot, string destZip) =>
        ZipDirectory(libraryRoot, destZip);

    /// <summary>
    /// Import a single .json or a .zip of .json files into <paramref name="targetCategory"/>.
    /// Each entry is validated independently; invalid ones are counted and skipped, never fatal.
    /// </summary>
    public static ImportResult Import(string libraryRoot, string sourcePath, string targetCategory, ConflictPolicy policy)
    {
        var result = new ImportResult();
        bool isZip = Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase);
        string tempDir = Path.Combine(Path.GetTempPath(), "QRK-Import", Guid.NewGuid().ToString("N"));
        string rootFull = Path.GetFullPath(libraryRoot);

        try
        {
            foreach (var (rel, tmp) in Extract(sourcePath, isZip, tempDir))
            {
                var probe = ResponseFile.Load(tmp, tempDir);
                if (!probe.IsValid)
                {
                    result.SkippedInvalid++;
                    result.Messages.Add($"Skipped invalid: {rel}");
                    continue;
                }

                string relTarget = string.IsNullOrEmpty(targetCategory) ? rel : targetCategory + "/" + rel;
                string dest = Path.GetFullPath(Path.Combine(rootFull, relTarget.Replace('/', Path.DirectorySeparatorChar)));

                // Never let a crafted entry (e.g. "..\..\evil.json" or a rooted path) escape the library.
                if (!IsInside(rootFull, dest))
                {
                    result.SkippedInvalid++;
                    result.Messages.Add($"Skipped unsafe path: {rel}");
                    continue;
                }

                if (File.Exists(dest))
                {
                    result.Conflicts++;
                    if (policy == ConflictPolicy.Skip)
                    {
                        result.Messages.Add($"Skipped (exists): {relTarget}");
                        continue;
                    }
                    if (policy == ConflictPolicy.KeepBoth) dest = NextFree(dest);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(tmp, dest, overwrite: true);
                result.Imported++;
                result.Messages.Add($"Imported: {Path.GetRelativePath(libraryRoot, dest).Replace('\\', '/')}");
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }

        return result;
    }

    /// <summary>Yields (relative source path, extracted temp file) for each .json to import.</summary>
    private static IEnumerable<(string rel, string tmp)> Extract(string sourcePath, bool isZip, string tempDir)
    {
        if (!isZip)
        {
            yield return (Path.GetFileName(sourcePath), sourcePath);
            yield break;
        }

        Directory.CreateDirectory(tempDir);
        using var zip = ZipFile.OpenRead(sourcePath);
        int i = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Length == 0) continue; // directory entry
            if (!entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            string tmp = Path.Combine(tempDir, $"{i++}.json");
            entry.ExtractToFile(tmp, overwrite: true);
            yield return (entry.FullName, tmp);
        }
    }

    private static void ZipDirectory(string sourceDir, string destZip)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destZip))!);
        if (File.Exists(destZip)) File.Delete(destZip);
        ZipFile.CreateFromDirectory(sourceDir, destZip, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    /// <summary>True if <paramref name="fullPath"/> is <paramref name="rootFull"/> or sits under it.</summary>
    private static bool IsInside(string rootFull, string fullPath)
    {
        string rel = Path.GetRelativePath(rootFull, fullPath);
        return !Path.IsPathRooted(rel)
            && rel != ".."
            && !rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string NextFree(string dest)
    {
        string dir = Path.GetDirectoryName(dest)!;
        string name = Path.GetFileNameWithoutExtension(dest);
        string ext = Path.GetExtension(dest);
        string candidate = Path.Combine(dir, $"{name} (imported){ext}");
        for (int n = 2; File.Exists(candidate); n++)
            candidate = Path.Combine(dir, $"{name} (imported {n}){ext}");
        return candidate;
    }
}
