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
}
