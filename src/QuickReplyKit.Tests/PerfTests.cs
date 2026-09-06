using System.Diagnostics;
using System.IO;
using QuickReplyKit.Core.Library;
using QuickReplyKit.Core.Search;

namespace QuickReplyKit.Tests;

/// <summary>
/// Large-library performance guard: a scan of ~4000 responses across nested folders followed by a
/// global search must finish well within an interactive budget. The bound is deliberately generous
/// (3s combined) so the test stays green on slow/CI hardware while still catching an accidental
/// O(n²) regression, which would blow past it by orders of magnitude.
/// </summary>
public class PerfTests
{
    private const int Folders = 40;      // top-level categories
    private const int SubPerFolder = 4;  // nested subfolders each
    private const int FilesPerSub = 25;  // → 40 * 4 * 25 = 4000 responses

    [Fact]
    public void ScanThenSearch_OnLargeLibrary_IsFast()
    {
        using var dir = new TempDir();
        int total = BuildLargeLibrary(dir.Of("lib"));
        Assert.Equal(Folders * SubPerFolder * FilesPerSub, total);

        var sw = Stopwatch.StartNew();
        var snap = LibraryScanner.Scan(dir.Of("lib"));
        // A couple of representative queries: a common token (many hits) and a rare one.
        var common = ResponseSearch.Search(snap.AllResponses, "response");
        var rare = ResponseSearch.Search(snap.AllResponses, "cat7 sub2");
        sw.Stop();

        Assert.Equal(total, snap.AllResponses.Count);
        Assert.NotEmpty(common);
        Assert.NotEmpty(rare);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"scan+search of {total} responses took {sw.ElapsedMilliseconds} ms (budget 3000 ms)");
    }

    /// <summary>Writes the response tree directly (minimal valid schema-v1 JSON) and returns the count.</summary>
    private static int BuildLargeLibrary(string root)
    {
        int n = 0;
        for (int f = 0; f < Folders; f++)
            for (int s = 0; s < SubPerFolder; s++)
            {
                string subDir = Path.Combine(root, $"Cat{f}", $"Sub{s}");
                Directory.CreateDirectory(subDir);
                for (int i = 0; i < FilesPerSub; i++)
                {
                    string json =
                        $"{{\"schema\":1,\"app\":\"App{f}\",\"tags\":[\"cat{f}\",\"sub{s}\"]," +
                        $"\"html\":\"<p>Response {f}-{s}-{i} body text</p>\",\"favorite\":false}}";
                    File.WriteAllText(Path.Combine(subDir, $"Response {f}-{s}-{i}.json"), json);
                    n++;
                }
            }
        return n;
    }
}
