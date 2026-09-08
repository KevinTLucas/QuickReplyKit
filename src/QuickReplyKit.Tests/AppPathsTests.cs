using System.IO;
using QuickReplyKit.Core;

namespace QuickReplyKit.Tests;

public class AppPathsTests
{
    [Fact]
    public void Defaults_ResolveFromCurrentWindowsProfile()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string documents = Path.Combine(profile, "Documents", "QuickReplyKit");
        string state = Path.Combine(appData, "QuickReplyKit");

        Assert.Equal(Path.Combine(documents, "Library"), AppPaths.DefaultLibraryDir);
        Assert.Equal(Path.Combine(documents, "Backups"), AppPaths.BackupsDir);
        Assert.Equal(Path.Combine(state, "config.json"), AppPaths.ConfigPath);
        Assert.Equal(Path.Combine(state, "logs"), AppPaths.LogsDir);
        Assert.Equal(Path.Combine(state, "trash"), AppPaths.TrashDir);
        Assert.Equal(Path.Combine(state, "script-trust.json"), AppPaths.ScriptTrustPath);
        Assert.All(new[]
        {
            AppPaths.DefaultLibraryDir, AppPaths.BackupsDir, AppPaths.ConfigPath,
            AppPaths.LogsDir, AppPaths.TrashDir, AppPaths.ScriptTrustPath,
        }, path => Assert.True(Path.IsPathFullyQualified(path), path));
    }
}
