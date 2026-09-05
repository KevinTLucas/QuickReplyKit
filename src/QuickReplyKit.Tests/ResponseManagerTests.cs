using System.IO;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Tests;

/// <summary>Headless file-level management over a throwaway temp library.</summary>
public class ResponseManagerTests : IDisposable
{
    private readonly string _root;

    public ResponseManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "qrk-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private static ResponseItem NewItem(string html = "<p>hi</p>") =>
        new() { Html = html, App = "IXL", Tags = new[] { "licensing" } };

    private ResponseItem Loaded(string path) => ResponseFile.Load(path, _root);

    [Fact]
    public void Create_WritesValidFileInNewNestedCategory()
    {
        var r = ResponseManager.Create(_root, "Clever Apps/Canvas", "No License", NewItem());

        Assert.True(r.Ok);
        Assert.True(File.Exists(r.Path));
        Assert.EndsWith(Path.Combine("Clever Apps", "Canvas", "No License.json"), r.Path);

        var loaded = Loaded(r.Path);
        Assert.True(loaded.IsValid);
        Assert.Equal("<p>hi</p>", loaded.Html);
        Assert.Equal("Clever Apps/Canvas", loaded.CategoryPath);
    }

    [Fact]
    public void Create_EmptyName_Rejected()
    {
        var r = ResponseManager.Create(_root, "", "   ", NewItem());
        Assert.Equal(ResultKind.InvalidName, r.Kind);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Create_SanitizesInvalidFilenameChars()
    {
        var r = ResponseManager.Create(_root, "", "a/b:c*d?", NewItem());
        Assert.True(r.Ok);
        Assert.Equal("abcd.json", Path.GetFileName(r.Path));
    }

    [Fact]
    public void Create_NameCollision_ReturnsConflictWithExistingPath()
    {
        var first = ResponseManager.Create(_root, "Cat", "Dup", NewItem());
        var second = ResponseManager.Create(_root, "Cat", "Dup", NewItem());

        Assert.Equal(ResultKind.Conflict, second.Kind);
        Assert.Equal(first.Path, second.Existing);
        Assert.True(File.Exists(second.Existing!));
    }

    [Fact]
    public void Duplicate_AddsCopySuffix_ThenNumbers()
    {
        var c = ResponseManager.Create(_root, "", "Note", NewItem());
        var item = Loaded(c.Path);

        var d1 = ResponseManager.Duplicate(item);
        Assert.True(d1.Ok);
        Assert.Equal("Note (copy).json", Path.GetFileName(d1.Path));

        var d2 = ResponseManager.Duplicate(item);
        Assert.Equal("Note (copy 2).json", Path.GetFileName(d2.Path));

        Assert.True(File.Exists(d1.Path));
        Assert.True(File.Exists(d2.Path));
    }

    [Fact]
    public void Rename_MovesFileToNewName()
    {
        var c = ResponseManager.Create(_root, "", "Old", NewItem());
        var item = Loaded(c.Path);

        var r = ResponseManager.Rename(item, "New");

        Assert.True(r.Ok);
        Assert.False(File.Exists(c.Path));
        Assert.True(File.Exists(r.Path));
        Assert.Equal("New.json", Path.GetFileName(r.Path));
    }

    [Fact]
    public void Rename_TargetExists_ReturnsConflict()
    {
        ResponseManager.Create(_root, "", "A", NewItem());
        var c = ResponseManager.Create(_root, "", "B", NewItem());
        var item = Loaded(c.Path);

        var r = ResponseManager.Rename(item, "A");
        Assert.Equal(ResultKind.Conflict, r.Kind);
        Assert.True(File.Exists(c.Path));   // original untouched
    }

    [Fact]
    public void Rename_EmptyName_Rejected()
    {
        var c = ResponseManager.Create(_root, "", "X", NewItem());
        var item = Loaded(c.Path);
        var r = ResponseManager.Rename(item, "??");   // all invalid chars -> empty
        Assert.Equal(ResultKind.InvalidName, r.Kind);
    }

    [Fact]
    public void Move_AcrossFolders_KeepsName()
    {
        var c = ResponseManager.Create(_root, "Src", "Item", NewItem());
        var item = Loaded(c.Path);

        var r = ResponseManager.Move(item, "Dest/Sub");

        Assert.True(r.Ok);
        Assert.False(File.Exists(c.Path));
        Assert.True(File.Exists(r.Path));
        Assert.EndsWith(Path.Combine("Dest", "Sub", "Item.json"), r.Path);
    }

    [Fact]
    public void Move_TargetExists_ReturnsConflict()
    {
        var a = ResponseManager.Create(_root, "Src", "Item", NewItem());
        ResponseManager.Create(_root, "Dest", "Item", NewItem());
        var item = Loaded(a.Path);

        var r = ResponseManager.Move(item, "Dest");
        Assert.Equal(ResultKind.Conflict, r.Kind);
        Assert.True(File.Exists(a.Path));   // original untouched
    }

    [Fact]
    public void Delete_RemovesFileFromItsPath()
    {
        var c = ResponseManager.Create(_root, "", "Trash", NewItem());
        var item = Loaded(c.Path);

        var r = ResponseManager.Delete(item);   // to Recycle Bin

        Assert.True(r.Ok);
        Assert.False(File.Exists(c.Path));
    }

    [Fact]
    public void CreateFolder_MakesNestedDirs()
    {
        var r = ResponseManager.CreateFolder(_root, "One/Two/Three");

        Assert.True(r.Ok);
        Assert.True(Directory.Exists(r.Path));
        Assert.True(Directory.Exists(Path.Combine(_root, "One", "Two", "Three")));
    }

    [Fact]
    public void CreateFolder_EmptyPath_Rejected()
    {
        var r = ResponseManager.CreateFolder(_root, "   ");
        Assert.Equal(ResultKind.InvalidName, r.Kind);
    }
}
