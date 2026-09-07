using System.IO;
using System.Threading.Tasks;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Core.Library;

/// <summary>
/// Walks the library root into a <see cref="CategoryNode"/> tree. Empty folders are kept,
/// invalid files are marked but never block siblings, a missing root yields an empty snapshot
/// (and creates nothing). Directories and files are sorted for a stable tree.
///
/// The tree/file enumeration is built first (cheap directory walk, deterministic pre-order), then
/// every <see cref="ResponseFile.Load"/> — which is pure/stateless — runs in parallel. The work is
/// I/O-bound (AV/OneDrive filter drivers on each file open), so parallel loads are the big win on a
/// large library. Results are reassembled in the original order, so the snapshot is byte-for-byte
/// identical (same items, same stable ordering, same invalid handling) to a sequential scan.
/// </summary>
public static class LibraryScanner
{
    private const string LegacyFolderSidecar = ".qrkfolder.json";

    public static LibrarySnapshot Scan(string libraryRoot)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (!Directory.Exists(libraryRoot))
        {
            Diag.Info("LibraryScanner", "Scan", $"complete rootAvailable=false responses=0 valid=0 invalid=0 elapsedMs={sw.ElapsedMilliseconds}");
            return Empty(libraryRoot);
        }

        // 1. Build the folder tree and collect every .json path in deterministic pre-order.
        var files = new List<(CategoryNode node, string path)>();
        var root = BuildNode(libraryRoot, libraryRoot, files);

        // 2. Load the files in parallel; index-keyed so the outcome is order-independent.
        var items = new ResponseItem[files.Count];
        Parallel.For(0, files.Count, i => items[i] = ResponseFile.Load(files[i].path, libraryRoot));

        // 3. Reassemble the tree and the flat list in the original pre-order.
        var all = new List<ResponseItem>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            files[i].node.Responses.Add(items[i]);
            all.Add(items[i]);
        }

        int valid = all.Count(x => x.IsValid);
        Diag.Info("LibraryScanner", "Scan",
            $"complete rootAvailable=true responses={all.Count} valid={valid} invalid={all.Count - valid} elapsedMs={sw.ElapsedMilliseconds}");
        return new LibrarySnapshot { Root = root, AllResponses = all, LibraryRoot = libraryRoot };
    }

    /// <summary>An empty snapshot rooted at <paramref name="libraryRoot"/> (creates nothing on disk).</summary>
    public static LibrarySnapshot Empty(string libraryRoot) => new()
    {
        Root = new CategoryNode { Name = SafeLeaf(libraryRoot), RelativePath = "" },
        AllResponses = new List<ResponseItem>(),
        LibraryRoot = libraryRoot,
    };

    private static CategoryNode BuildNode(string dir, string libraryRoot, List<(CategoryNode, string)> files)
    {
        string rel = Path.GetRelativePath(libraryRoot, dir);
        bool isRoot = rel == ".";
        var node = new CategoryNode
        {
            Name = isRoot ? SafeLeaf(libraryRoot) : Path.GetFileName(dir),
            RelativePath = isRoot ? "" : rel.Replace('\\', '/'),
        };

        foreach (var file in Directory.EnumerateFiles(dir, "*.json")
                     .Where(f => !Path.GetFileName(f).Equals(LegacyFolderSidecar, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            files.Add((node, file));

        foreach (var sub in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            node.Children.Add(BuildNode(sub, libraryRoot, files));

        return node;
    }

    private static string SafeLeaf(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
}
