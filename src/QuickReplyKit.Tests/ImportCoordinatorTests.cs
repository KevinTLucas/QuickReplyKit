using System.IO;
using QuickReplyKit.Core.ImportExport;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Tests;

/// <summary>
/// The UI-side import glue: target-path building and the read-only conflict peek that decides
/// whether the launcher prompts for a batch policy. Plus an end-to-end export→delete→re-import
/// round trip driven through <see cref="LibraryPorter"/> (the "restore a deleted folder" scenario).
/// </summary>
public class ImportCoordinatorTests
{
    [Theory]
    [InlineData("", "Hi.json", "Hi.json")]
    [InlineData("Support", "Hi.json", "Support/Hi.json")]
    [InlineData("Support/General", "Sub/Hi.json", "Support/General/Sub/Hi.json")]
    public void TargetPath_JoinsCategoryAndEntry(string category, string entry, string expected) =>
        Assert.Equal(expected, ImportCoordinator.TargetPath(category, entry));

    [Fact]
    public void CountConflicts_SingleJson_ZeroWhenTargetFolderEmpty()
    {
        using var stage = new TempDir();
        using var lib = new TempDir();
        TestFiles.WriteResponse(stage.Of("Hi.json"));

        Assert.Equal(0, ImportCoordinator.CountConflicts(lib.Path, stage.Of("Hi.json"), "Support"));
    }

    [Fact]
    public void CountConflicts_SingleJson_CountsExistingAtTarget()
    {
        using var stage = new TempDir();
        using var lib = new TempDir();
        TestFiles.WriteResponse(stage.Of("Hi.json"));
        TestFiles.WriteResponse(lib.Of("Support", "Hi.json"));   // collision under the chosen category

        Assert.Equal(1, ImportCoordinator.CountConflicts(lib.Path, stage.Of("Hi.json"), "Support"));
        Assert.Equal(0, ImportCoordinator.CountConflicts(lib.Path, stage.Of("Hi.json"), "Other"));
    }

    [Fact]
    public void CountConflicts_Zip_CountsOnlyEntriesThatAlreadyExist()
    {
        using var lib = new TempDir();
        TestFiles.WriteResponse(lib.Of("IXL", "Top.json"));
        TestFiles.WriteResponse(lib.Of("IXL", "Licensing", "No License.json"));
        using var stage = new TempDir();
        string zip = stage.Of("IXL.zip");
        LibraryPorter.ExportFolder(lib.Path, "IXL", zip);

        // Re-importing the zip back under "IXL" collides with both existing files.
        Assert.Equal(2, ImportCoordinator.CountConflicts(lib.Path, zip, "IXL"));
        // Into a fresh category nothing collides.
        Assert.Equal(0, ImportCoordinator.CountConflicts(lib.Path, zip, "Backup"));
    }

    [Fact]
    public void ExportFolder_DeleteSubtree_ReImport_RestoresIdenticalHierarchy()
    {
        using var lib = new TempDir();
        TestFiles.WriteResponse(lib.Of("IXL", "Top.json"), html: "<p>top</p>");
        TestFiles.WriteResponse(lib.Of("IXL", "Licensing", "No License.json"), html: "<p>none</p>");

        using var stage = new TempDir();
        string zip = stage.Of("IXL.zip");
        LibraryPorter.ExportFolder(lib.Path, "IXL", zip);

        Directory.Delete(lib.Of("IXL"), recursive: true);
        Assert.False(Directory.Exists(lib.Of("IXL")));

        // No conflicts after deletion, so the launcher would import straight through.
        Assert.Equal(0, ImportCoordinator.CountConflicts(lib.Path, zip, "IXL"));
        var r = LibraryPorter.Import(lib.Path, zip, "IXL", ConflictPolicy.Skip);

        Assert.Equal(2, r.Imported);
        Assert.Equal("<p>top</p>", ResponseFile.Load(lib.Of("IXL", "Top.json"), lib.Path).Html);
        Assert.Equal("<p>none</p>",
            ResponseFile.Load(lib.Of("IXL", "Licensing", "No License.json"), lib.Path).Html);
    }
}
