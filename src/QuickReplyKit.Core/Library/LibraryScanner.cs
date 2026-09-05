using System.IO;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Core.Library;

/// <summary>
/// Walks the library root into a <see cref="CategoryNode"/> tree. Empty folders are kept,
/// invalid files are marked but never block siblings, a missing root yields an empty snapshot
/// (and creates nothing). Directories and files are sorted for a stable tree.
/// </summary>
public static class LibraryScanner
{
    public static LibrarySnapshot Scan(string libraryRoot)
    {
        var all = new List<ResponseItem>();

        if (!Directory.Exists(libraryRoot))
        {
            return new LibrarySnapshot
            {
                Root = new CategoryNode { Name = SafeLeaf(libraryRoot), RelativePath = "" },
                AllResponses = all,
                LibraryRoot = libraryRoot,
            };
        }

        var root = BuildNode(libraryRoot, libraryRoot, all);
        return new LibrarySnapshot { Root = root, AllResponses = all, LibraryRoot = libraryRoot };
    }

    private static CategoryNode BuildNode(string dir, string libraryRoot, List<ResponseItem> all)
    {
        string rel = Path.GetRelativePath(libraryRoot, dir);
        bool isRoot = rel == ".";
        var node = new CategoryNode
        {
            Name = isRoot ? SafeLeaf(libraryRoot) : Path.GetFileName(dir),
            RelativePath = isRoot ? "" : rel.Replace('\\', '/'),
        };

        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var item = ResponseFile.Load(file, libraryRoot);
            node.Responses.Add(item);
            all.Add(item);
        }

        foreach (var sub in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            node.Children.Add(BuildNode(sub, libraryRoot, all));

        return node;
    }

    private static string SafeLeaf(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
}
