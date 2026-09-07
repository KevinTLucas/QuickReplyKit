using System.IO;

namespace QuickReplyKit.Core.Storage;

/// <summary>Validation shared by Settings and every backup operation.</summary>
public static class BackupLocation
{
    public static string EnsureWritable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new IOException("The backup folder must be an absolute path.");

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new IOException("The backup folder path is invalid.", ex);
        }

        if (File.Exists(full)) throw new IOException("The backup folder path points to a file.");

        string probe = Path.Combine(full, ".qrk-write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(full);
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            throw new IOException(
                "The backup folder is unavailable or not writable. Check the path and your access, then try again.", ex);
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { /* best effort */ }
        }

        return full;
    }
}
