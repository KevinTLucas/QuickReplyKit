using System.Diagnostics;
using System.IO;
using QuickReplyKit.Core.Library;
using QuickReplyKit.Core.Model;

namespace QuickReplyKit.Tests;

public class LibraryScannerTests
{
    [Fact]
    public void Scan_BuildsNestedTree_AndFlatList()
    {
        using var t = new TempDir();
        TestFiles.WriteResponse(t.Of("General", "Ticket.json"));
        TestFiles.WriteResponse(t.Of("Clever Apps", "IXL", "Getting Started.json"));
        TestFiles.WriteResponse(t.Of("Clever Apps", "IXL", "Licensing", "No License.json"));
        Directory.CreateDirectory(t.Of("Empty")); // empty category preserved

        var snap = LibraryScanner.Scan(t.Path);

        Assert.Equal(3, snap.AllResponses.Count);
        Assert.Equal(3, snap.ValidResponses.Count());

        // Root children sorted: "Clever Apps", "Empty", "General"
        Assert.Equal(new[] { "Clever Apps", "Empty", "General" }, snap.Root.Children.Select(c => c.Name));

        var empty = snap.Root.Children.Single(c => c.Name == "Empty");
        Assert.Empty(empty.Responses);
        Assert.Empty(empty.Children);

        var clever = snap.Root.Children.Single(c => c.Name == "Clever Apps");
        var ixl = clever.Children.Single(c => c.Name == "IXL");
        Assert.Equal("Clever Apps/IXL", ixl.RelativePath);
        Assert.Single(ixl.Responses); // "Getting Started" directly in IXL
        Assert.Equal("Licensing", ixl.Children.Single().Name);

        var licensed = ixl.Children.Single();
        Assert.Equal("Clever Apps/IXL/Licensing", licensed.Responses.Single().CategoryPath);
    }

    [Fact]
    public void Scan_InvalidFile_DoesNotBlockSiblings()
    {
        using var t = new TempDir();
        TestFiles.WriteResponse(t.Of("Good.json"));
        File.WriteAllText(t.Of("Bad.json"), "{ broken");

        var snap = LibraryScanner.Scan(t.Path);

        Assert.Equal(2, snap.AllResponses.Count);
        Assert.Single(snap.ValidResponses);
        Assert.Contains(snap.AllResponses, r => r.Name == "Good" && r.IsValid);
        Assert.Contains(snap.AllResponses, r => r.Name == "Bad" && !r.IsValid);
    }

    [Fact]
    public void Scan_NonExistentRoot_ReturnsEmpty_CreatesNothing()
    {
        using var t = new TempDir();
        string missing = t.Of("does-not-exist");

        var snap = LibraryScanner.Scan(missing);

        Assert.Empty(snap.AllResponses);
        Assert.NotNull(snap.Root);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Scan_ParallelLoad_MatchesSequentialReference_AndIsDeterministic()
    {
        // A tree with nested folders, files at every level, and invalid files interleaved. The parallel
        // scan must yield exactly the same flat list (items + order + valid flags) as a straightforward
        // sequential pre-order walk, and the same result on every run.
        using var t = new TempDir();
        TestFiles.WriteResponse(t.Of("root-b.json"));
        TestFiles.WriteResponse(t.Of("root-a.json"));
        File.WriteAllText(t.Of("root-bad.json"), "{ broken");
        TestFiles.WriteResponse(t.Of("Beta", "b2.json"));
        TestFiles.WriteResponse(t.Of("Beta", "b1.json"));
        File.WriteAllText(t.Of("Beta", "b-bad.json"), "{\"schema\":1}"); // no html → invalid
        TestFiles.WriteResponse(t.Of("Alpha", "Sub", "deep.json"));
        TestFiles.WriteResponse(t.Of("Alpha", "a1.json"));
        Directory.CreateDirectory(t.Of("Empty"));

        var expected = SequentialReference(t.Path, t.Path).ToList();

        // Run several times; every scan must equal the reference and each other.
        for (int run = 0; run < 5; run++)
        {
            var snap = LibraryScanner.Scan(t.Path);
            var actual = snap.AllResponses
                .Select(r => (r.RelativePath, r.IsValid))
                .ToList();
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// Independent, obviously-correct sequential pre-order walk used as the oracle for the parallel scan:
    /// files in a folder sorted OrdinalIgnoreCase, then subfolders sorted the same way, depth-first.
    /// </summary>
    private static IEnumerable<(string RelativePath, bool IsValid)> SequentialReference(string dir, string root)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var item = QuickReplyKit.Core.Storage.ResponseFile.Load(file, root);
            yield return (item.RelativePath, item.IsValid);
        }
        foreach (var sub in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            foreach (var r in SequentialReference(sub, root))
                yield return r;
    }

    [Fact]
    public void Scan_LargeLibrary_IsFast()
    {
        using var t = new TempDir();
        const int folders = 20, perFolder = 100; // 2000 files
        for (int f = 0; f < folders; f++)
            for (int i = 0; i < perFolder; i++)
                TestFiles.WriteResponse(t.Of($"Folder{f:D2}", $"Item{i:D3}.json"));

        var sw = Stopwatch.StartNew();
        var snap = LibraryScanner.Scan(t.Path);
        sw.Stop();

        Assert.Equal(folders * perFolder, snap.AllResponses.Count);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"scan took {sw.ElapsedMilliseconds} ms");
    }
}
