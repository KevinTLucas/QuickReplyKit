using System.IO;

namespace QuickReplyKit.Tests;

public class SelfTestHarnessTests
{
    [Fact]
    public void HarnessUsesOwnedChildTarget_NotNotepadDiscovery()
    {
        string code = File.ReadAllText(TestFiles.RepoAsset("src/QuickReplyKit/SelfTest.cs"));

        Assert.Contains("--selftest-target", code, StringComparison.Ordinal);
        Assert.Contains("CreateWindowEx", code, StringComparison.Ordinal);
        Assert.DoesNotContain("notepad.exe", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FindNotepad", code, StringComparison.Ordinal);
        Assert.DoesNotContain("QRK_Notepad", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildTargetRouteRunsBeforeSingleInstanceSetup()
    {
        string code = File.ReadAllText(TestFiles.RepoAsset("src/QuickReplyKit/App.xaml.cs"));

        int target = code.IndexOf("--selftest-target", StringComparison.Ordinal);
        int mutex = code.IndexOf("new Mutex", StringComparison.Ordinal);
        Assert.True(target >= 0 && target < mutex);
    }
}
