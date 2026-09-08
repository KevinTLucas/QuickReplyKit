using System.IO;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Documents;

namespace QuickReplyKit.Tests;

public class LauncherWindowTests
{
    [Fact]
    public void FindAncestor_HandlesRunInsideVisualTree()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var run = new Run("match");
                var text = new TextBlock();
                text.Inlines.Add(run);
                var border = new Border { Child = text };

                Assert.Same(text, LauncherWindow.FindAncestor<TextBlock>(run));
                Assert.Same(border, LauncherWindow.FindAncestor<Border>(run));
                Assert.Null(LauncherWindow.FindAncestor<ListBoxItem>(run));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    [Fact]
    public void RunImportSafely_ContainsFailure_AndLeavesExistingLibraryUsable()
    {
        using var temp = new TempDir();
        string response = temp.Of("Library", "Existing.json");
        TestFiles.WriteResponse(response, "<p>unchanged</p>", "Existing");
        string before = File.ReadAllText(response);
        Exception? reported = null;

        bool completed = LauncherWindow.RunImportSafely(
            () => throw new IOException("simulated import failure"),
            ex => reported = ex);

        Assert.False(completed);
        Assert.IsType<IOException>(reported);
        Assert.Equal(before, File.ReadAllText(response));
        Assert.True(LauncherWindow.RunImportSafely(() => { }, _ => { }));
    }

    [Fact]
    public void RunImportSafely_ContainsFailurePresenterFailure()
    {
        bool completed = LauncherWindow.RunImportSafely(
            () => throw new InvalidDataException("bad import"),
            _ => throw new InvalidOperationException("dialog failed"));

        Assert.False(completed);
    }
}
