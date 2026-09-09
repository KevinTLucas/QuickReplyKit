using System.IO;

namespace QuickReplyKit.Tests;

public class PowerShellFeatureRemovalTests
{
    [Fact]
    public void ProductSourceAndSeedLibrary_ContainNoNativeScriptRuntime()
    {
        string[] roots =
        [
            TestFiles.RepoAsset("src/QuickReplyKit.Core"),
            TestFiles.RepoAsset("src/QuickReplyKit"),
            TestFiles.RepoAsset("assets/DefaultLibrary"),
            TestFiles.RepoAsset("release/DefaultLibrary"),
        ];
        string[] forbidden = ["powershell-1", "NativeScriptRunner", "QRK_LIBRARY_ROOT", "full-trust"];

        foreach (string path in roots.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            string content;
            try { content = File.ReadAllText(path); }
            catch { continue; }
            Assert.DoesNotContain(forbidden, value =>
                content.Contains(value, StringComparison.OrdinalIgnoreCase));
        }
    }
}
