using System.IO;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using QuickReplyKit.Core.Model;

namespace QuickReplyKit.Core.Storage;

public enum ResultKind { Ok, InvalidName, Conflict }

/// <summary>
/// Outcome of a <see cref="ResponseManager"/> operation.
/// <list type="bullet">
/// <item><c>Ok</c> — <see cref="Path"/> is the resulting file/folder path.</item>
/// <item><c>Conflict</c> — <see cref="Existing"/> is the file already occupying the target;
/// the caller decides overwrite / keep-both / cancel (this class never prompts).</item>
/// <item><c>InvalidName</c> — <see cref="Error"/> explains why the name was rejected.</item>
/// </list>
/// </summary>
public sealed record ManageResult(ResultKind Kind, string Path, string? Existing = null, string? Error = null)
{
    public bool Ok => Kind == ResultKind.Ok;

    public static ManageResult Success(string path) => new(ResultKind.Ok, path);
    public static ManageResult Invalid(string reason) => new(ResultKind.InvalidName, "", Error: reason);
    public static ManageResult Conflict(string existing) => new(ResultKind.Conflict, "", Existing: existing);
}

/// <summary>
/// Headless, file-level response management. Identity is the filename; folders are categories.
/// Names are sanitized to valid filenames; empty/invalid names are rejected. Target-folder name
/// collisions are reported (not resolved). Deletes go to the Recycle Bin so they are recoverable.
/// </summary>
public static class ResponseManager
{
    private static readonly char[] InvalidFileChars = Path.GetInvalidFileNameChars();

    /// <summary>Create a new response file. Fails with a conflict if the name is taken in the
    /// target category; the category (any depth, may be new) is created as needed.</summary>
    public static ManageResult Create(string libraryRoot, string categoryPath, string name, ResponseItem item)
    {
        string? clean = Sanitize(name);
        if (clean is null) return ManageResult.Invalid("Name is empty or contains only invalid characters.");

        string category = SanitizeCategory(categoryPath);
        string target = Path.Combine(FolderPath(libraryRoot, category), clean + ".json");
        if (File.Exists(target)) return ManageResult.Conflict(target);

        item.Name = clean;
        item.CategoryPath = category;
        ResponseFile.Save(item, libraryRoot);   // creates the folder and stamps FullPath
        return ManageResult.Success(item.FullPath);
    }

    /// <summary>Copy a response beside itself as "Name (copy).json" (next free suffix).</summary>
    public static ManageResult Duplicate(ResponseItem item)
    {
        string dir = Path.GetDirectoryName(item.FullPath)!;
        string target = NextFreeCopy(dir, item.Name);
        File.Copy(item.FullPath, target);
        return ManageResult.Success(target);
    }

    /// <summary>Rename within the same folder. Conflict if another file has the new name.</summary>
    public static ManageResult Rename(ResponseItem item, string newName)
    {
        string? clean = Sanitize(newName);
        if (clean is null) return ManageResult.Invalid("Name is empty or contains only invalid characters.");

        string dir = Path.GetDirectoryName(item.FullPath)!;
        string target = Path.Combine(dir, clean + ".json");
        if (PathEquals(target, item.FullPath)) return ManageResult.Success(item.FullPath);
        if (File.Exists(target)) return ManageResult.Conflict(target);

        File.Move(item.FullPath, target);
        return ManageResult.Success(target);
    }

    /// <summary>Move to another category (created as needed), keeping the filename. Conflict if a
    /// same-named file already lives there.</summary>
    public static ManageResult Move(ResponseItem item, string newCategoryPath)
    {
        string root = LibraryRoot(item);
        string category = SanitizeCategory(newCategoryPath);
        string folder = FolderPath(root, category);
        Directory.CreateDirectory(folder);

        string target = Path.Combine(folder, item.Name + ".json");
        if (PathEquals(target, item.FullPath)) return ManageResult.Success(item.FullPath);
        if (File.Exists(target)) return ManageResult.Conflict(target);

        File.Move(item.FullPath, target);
        return ManageResult.Success(target);
    }

    /// <summary>Send the response file to the Recycle Bin (recoverable).</summary>
    public static ManageResult Delete(ResponseItem item)
    {
        FileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        return ManageResult.Success(item.FullPath);
    }

    /// <summary>Create a (possibly nested) category folder.</summary>
    public static ManageResult CreateFolder(string libraryRoot, string categoryPath)
    {
        string category = SanitizeCategory(categoryPath);
        if (category.Length == 0) return ManageResult.Invalid("Category path is empty or invalid.");

        string folder = FolderPath(libraryRoot, category);
        Directory.CreateDirectory(folder);
        return ManageResult.Success(folder);
    }

    /// <summary>Strip characters invalid in a filename; return null for an empty/invalid result.</summary>
    public static string? Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var sb = new StringBuilder(name.Length);
        foreach (char c in name.Trim())
            if (Array.IndexOf(InvalidFileChars, c) < 0) sb.Append(c);
        string cleaned = sb.ToString().TrimEnd('.', ' ').Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static string SanitizeCategory(string? categoryPath)
    {
        if (string.IsNullOrWhiteSpace(categoryPath)) return "";
        var parts = categoryPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var clean = parts.Select(Sanitize).Where(p => p != null).Select(p => p!);
        return string.Join('/', clean);
    }

    private static string FolderPath(string root, string category) =>
        category.Length == 0 ? root : Path.Combine(root, category.Replace('/', Path.DirectorySeparatorChar));

    private static string NextFreeCopy(string dir, string baseName)
    {
        string candidate = Path.Combine(dir, $"{baseName} (copy).json");
        if (!File.Exists(candidate)) return candidate;
        for (int n = 2; ; n++)
        {
            candidate = Path.Combine(dir, $"{baseName} (copy {n}).json");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // Recover the library root from an item: FullPath = root \ RelativePath.
    private static string LibraryRoot(ResponseItem item)
    {
        string relNative = item.RelativePath.Replace('/', Path.DirectorySeparatorChar);
        if (!string.IsNullOrEmpty(relNative) &&
            item.FullPath.EndsWith(relNative, StringComparison.OrdinalIgnoreCase))
            return item.FullPath[..^relNative.Length].TrimEnd(Path.DirectorySeparatorChar);

        // Fallback: walk up from the file's folder by the category depth.
        string dir = Path.GetDirectoryName(item.FullPath)!;
        int depth = string.IsNullOrEmpty(item.CategoryPath) ? 0 : item.CategoryPath.Split('/').Length;
        for (int i = 0; i < depth; i++) dir = Path.GetDirectoryName(dir)!;
        return dir;
    }
}
