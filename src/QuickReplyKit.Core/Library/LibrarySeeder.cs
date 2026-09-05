using System.IO;

namespace QuickReplyKit.Core.Library;

/// <summary>
/// First-run seeding: if the library has no responses yet, copy the bundled default library
/// into it. No-op if the source is missing or the library already has any .json.
/// </summary>
public static class LibrarySeeder
{
    public static void SeedIfEmpty(string libraryRoot, string sourceDefaultLibraryDir)
    {
        if (!Directory.Exists(sourceDefaultLibraryDir)) return;

        bool hasResponses = Directory.Exists(libraryRoot)
            && Directory.EnumerateFiles(libraryRoot, "*.json", SearchOption.AllDirectories).Any();
        if (hasResponses) return;

        CopyTree(sourceDefaultLibraryDir, libraryRoot);
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyTree(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }
}
