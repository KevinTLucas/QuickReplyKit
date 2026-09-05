using System.IO;
using System.Text.Json;

namespace QuickReplyKit.Tests;

/// <summary>Throwaway temp directory, deleted on Dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "QRK-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Of(params string[] parts) =>
        System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }

    public override string ToString() => Path;
}

internal static class TestFiles
{
    /// <summary>Writes a minimal valid schema-v1 response file (creating parent dirs).</summary>
    public static void WriteResponse(string fullPath, string html = "<p>x</p>", string app = "",
        bool favorite = false, string[]? tags = null)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        var obj = new
        {
            schema = 1,
            app,
            tags = tags ?? Array.Empty<string>(),
            html,
            favorite,
            created = "2026-01-01T00:00:00Z",
            modified = "2026-01-01T00:00:00Z",
        };
        File.WriteAllText(fullPath, JsonSerializer.Serialize(obj));
    }

    /// <summary>Walks up from the test binary to the repo root (folder holding ARCHITECTURE.md).</summary>
    public static string RepoAsset(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "ARCHITECTURE.md")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return System.IO.Path.Combine(dir!.FullName, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }
}
