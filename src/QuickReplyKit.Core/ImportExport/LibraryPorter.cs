using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Core.ImportExport;

public enum ConflictPolicy { Overwrite, KeepBoth, Skip }

public sealed class ImportResult
{
    public int Imported { get; internal set; }
    public int SkippedInvalid { get; internal set; }
    public int Conflicts { get; internal set; }
    public List<string> Messages { get; } = new();
    public List<ImportOutcome> Outcomes { get; } = new();
}

/// <summary>Validated import/export for a single response or response ZIP.</summary>
public static class LibraryPorter
{
    public const int MaxImportEntries = 10_000;
    public const long MaxEntryBytes = 10L * 1024 * 1024;
    public const long MaxImportBytes = 100L * 1024 * 1024;
    private const string FolderSidecar = ".qrkfolder.json";

    private sealed record Candidate(string Path, byte[] Content, string? Error = null);

    public static void Export(ResponseItem item, string destPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destPath))!);
        File.Copy(item.FullPath, destPath, overwrite: true);
    }

    public static void ExportFolder(string libraryRoot, string categoryPath, string destZip)
    {
        string source = string.IsNullOrEmpty(categoryPath)
            ? libraryRoot
            : Path.Combine(libraryRoot, categoryPath.Replace('/', Path.DirectorySeparatorChar));
        ZipDirectory(source, destZip);
    }

    public static void ExportLibrary(string libraryRoot, string destZip) => ZipDirectory(libraryRoot, destZip);

    /// <summary>Build the exact validated plan that the preview displays and the importer executes.</summary>
    public static ImportPlan PlanImport(string libraryRoot, string sourcePath, string targetCategory)
    {
        string root = Path.GetFullPath(libraryRoot);
        string source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("Import source not found.", source);

        byte[] sourceHash = HashFile(source);
        var candidates = ReadCandidates(source);
        if (!sourceHash.AsSpan().SequenceEqual(HashFile(source)))
            throw new IOException("The import source changed while it was being read. Try again.");

        var existing = ExistingContent(root);
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ImportPlanEntry>();

        foreach (var candidate in candidates)
        {
            int id = entries.Count;
            string display = JoinPath(targetCategory, candidate.Path);
            if (!TryDestination(root, targetCategory, candidate.Path, out string relative, out string full,
                    out string? pathError))
            {
                entries.Add(Entry(id, candidate.Path, display, ImportClassification.Unsafe,
                    pathError ?? "Unsafe destination path."));
                continue;
            }
            if (!reserved.Add(full))
            {
                entries.Add(Entry(id, candidate.Path, relative, ImportClassification.Unsafe,
                    "Another incoming entry resolves to the same destination."));
                continue;
            }
            if (candidate.Error is not null)
            {
                entries.Add(Entry(id, candidate.Path, relative, ImportClassification.Invalid, candidate.Error));
                continue;
            }
            if (!ResponseFile.Validate(candidate.Content, out string? validationError))
            {
                entries.Add(Entry(id, candidate.Path, relative, ImportClassification.Invalid,
                    validationError ?? "Invalid response file."));
                continue;
            }

            byte[] contentHash = SHA256.HashData(candidate.Content);
            byte[]? destinationHash = File.Exists(full) ? HashFile(full) : null;
            ImportClassification classification;
            ImportAction action;
            string? match = null;
            string? keepBoth = null;

            if (destinationHash is not null && destinationHash.AsSpan().SequenceEqual(contentHash)
                && File.ReadAllBytes(full).AsSpan().SequenceEqual(candidate.Content))
            {
                classification = ImportClassification.Identical;
                action = ImportAction.Skip;
                match = relative;
            }
            else if (destinationHash is not null)
            {
                classification = ImportClassification.Conflict;
                action = ImportAction.Skip;
                keepBoth = NextFree(full, reserved);
                reserved.Add(keepBoth);
            }
            else if (FindExact(existing, contentHash, candidate.Content) is { } duplicate)
            {
                classification = ImportClassification.DuplicateElsewhere;
                action = ImportAction.Skip;
                match = Path.GetRelativePath(root, duplicate).Replace('\\', '/');
            }
            else
            {
                classification = ImportClassification.New;
                action = ImportAction.Import;
            }

            entries.Add(new ImportPlanEntry(id, candidate.Path, relative, classification, action, match, null,
                candidate.Content, full, destinationHash, keepBoth));
        }

        return new ImportPlan(root, source, sourceHash, entries);
    }

    /// <summary>Execute the immutable preview plan with optional per-row decisions.</summary>
    public static ImportResult Import(ImportPlan plan, IReadOnlyDictionary<int, ImportAction>? decisions = null)
    {
        var result = new ImportResult
        {
            Conflicts = plan.Entries.Count(e => e.Classification == ImportClassification.Conflict),
        };
        bool sourceStale = !File.Exists(plan.SourcePath)
            || !plan.SourceHash.AsSpan().SequenceEqual(HashFile(plan.SourcePath));

        var actions = plan.Entries.ToDictionary(e => e.Id,
            e => NormalizeAction(e, decisions is not null && decisions.TryGetValue(e.Id, out var chosen)
                ? chosen : e.DefaultAction));
        var stale = new HashSet<int>();
        if (sourceStale)
        {
            foreach (var entry in plan.Entries.Where(e => actions[e.Id] != ImportAction.Skip)) stale.Add(entry.Id);
        }
        else
        {
            foreach (var entry in plan.Entries.Where(e => actions[e.Id] != ImportAction.Skip))
            {
                if (entry.DestinationFullPath is null
                    || !DestinationMatches(plan.LibraryRoot, entry.DestinationFullPath, entry.DestinationHash)
                    || actions[entry.Id] == ImportAction.KeepBoth
                        && (entry.KeepBothFullPath is null
                            || !DestinationMatches(plan.LibraryRoot, entry.KeepBothFullPath, null)))
                    stale.Add(entry.Id);
            }
        }

        foreach (var entry in plan.Entries)
        {
            ImportOutcome outcome;
            if (entry.Classification == ImportClassification.Invalid)
                outcome = new(entry.Id, entry.DestinationPath, ImportOutcomeKind.Invalid,
                    entry.Message ?? "Invalid response file.");
            else if (entry.Classification == ImportClassification.Unsafe)
                outcome = new(entry.Id, entry.DestinationPath, ImportOutcomeKind.Unsafe,
                    entry.Message ?? "Unsafe import entry.");
            else if (stale.Contains(entry.Id))
                outcome = new(entry.Id, entry.DestinationPath, ImportOutcomeKind.Stale,
                    "Source or destination changed after preview; skipped.");
            else if (actions[entry.Id] == ImportAction.Skip)
                outcome = new(entry.Id, entry.DestinationPath, ImportOutcomeKind.Skipped,
                    entry.Classification == ImportClassification.Identical ? "Identical; no changes made." : "Skipped.");
            else
                outcome = Write(entry, actions[entry.Id]);

            result.Outcomes.Add(outcome);
            result.Messages.Add(outcome.Message);
            if (outcome.Kind is ImportOutcomeKind.Imported or ImportOutcomeKind.Overwritten or ImportOutcomeKind.KeptBoth)
                result.Imported++;
            if (outcome.Kind is ImportOutcomeKind.Invalid or ImportOutcomeKind.Unsafe)
                result.SkippedInvalid++;
        }
        return result;
    }

    /// <summary>Compatibility wrapper for callers that choose one policy for every conflict.</summary>
    public static ImportResult Import(string libraryRoot, string sourcePath, string targetCategory, ConflictPolicy policy)
    {
        var plan = PlanImport(libraryRoot, sourcePath, targetCategory);
        var decisions = plan.Entries.ToDictionary(e => e.Id, e => e.Classification switch
        {
            ImportClassification.New or ImportClassification.DuplicateElsewhere => ImportAction.Import,
            ImportClassification.Conflict => policy switch
            {
                ConflictPolicy.Overwrite => ImportAction.Overwrite,
                ConflictPolicy.KeepBoth => ImportAction.KeepBoth,
                _ => ImportAction.Skip,
            },
            _ => ImportAction.Skip,
        });
        return Import(plan, decisions);
    }

    private static ImportPlanEntry Entry(int id, string source, string destination,
        ImportClassification classification, string message) =>
        new(id, source, destination, classification, ImportAction.Skip, null, message,
            Array.Empty<byte>(), null, null, null);

    private static ImportAction NormalizeAction(ImportPlanEntry entry, ImportAction action) => entry.Classification switch
    {
        ImportClassification.New or ImportClassification.DuplicateElsewhere =>
            action == ImportAction.Import ? action : ImportAction.Skip,
        ImportClassification.Conflict => action is ImportAction.Overwrite or ImportAction.KeepBoth ? action : ImportAction.Skip,
        _ => ImportAction.Skip,
    };

    private static ImportOutcome Write(ImportPlanEntry entry, ImportAction action)
    {
        string destination = action == ImportAction.KeepBoth ? entry.KeepBothFullPath! : entry.DestinationFullPath!;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temp, entry.Content);
                File.Move(temp, destination, action == ImportAction.Overwrite);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ } }

            var kind = action switch
            {
                ImportAction.Overwrite => ImportOutcomeKind.Overwritten,
                ImportAction.KeepBoth => ImportOutcomeKind.KeptBoth,
                _ => ImportOutcomeKind.Imported,
            };
            string relative = Path.GetRelativePath(Path.GetPathRoot(destination)!, destination);
            return new(entry.Id, entry.DestinationPath, kind, $"{kind}: {entry.DestinationPath}");
        }
        catch (Exception ex)
        {
            return new(entry.Id, entry.DestinationPath, ImportOutcomeKind.Failed,
                $"Failed: {entry.DestinationPath} ({ex.Message})");
        }
    }

    private static bool DestinationMatches(string root, string path, byte[]? expectedHash)
    {
        if (!IsInside(root, path) || HasReparsePoint(root, path)) return false;
        if (expectedHash is null) return !File.Exists(path) && !Directory.Exists(path);
        return File.Exists(path) && expectedHash.AsSpan().SequenceEqual(HashFile(path));
    }

    private static IReadOnlyList<Candidate> ReadCandidates(string source)
    {
        bool zipSource = Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase);
        if (!zipSource)
        {
            if (!Path.GetExtension(source).Equals(".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Only .json response files and .zip archives can be imported.");
            if (Path.GetFileName(source).Equals(FolderSidecar, StringComparison.OrdinalIgnoreCase))
                return Array.Empty<Candidate>();
            long size = new FileInfo(source).Length;
            return new[] { size > MaxEntryBytes
                ? new Candidate(Path.GetFileName(source), Array.Empty<byte>(), "Response exceeds the 10 MB limit.")
                : new Candidate(Path.GetFileName(source), File.ReadAllBytes(source)) };
        }

        using var zip = ZipFile.OpenRead(source);
        if (zip.Entries.Count > MaxImportEntries)
            return new[] { new Candidate("Archive", Array.Empty<byte>(),
                $"Archive exceeds the {MaxImportEntries:N0}-entry limit.") };

        var candidates = new List<Candidate>();
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Length == 0 || !entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || entry.Name.Equals(FolderSidecar, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Length > MaxEntryBytes)
            {
                candidates.Add(new(entry.FullName, Array.Empty<byte>(), "Response exceeds the 10 MB limit."));
                continue;
            }
            byte[] content;
            try { using var stream = entry.Open(); content = ReadLimited(stream, MaxEntryBytes); }
            catch (InvalidDataException ex) { candidates.Add(new(entry.FullName, Array.Empty<byte>(), ex.Message)); continue; }
            if (total > MaxImportBytes - content.Length)
            {
                candidates.Add(new(entry.FullName, Array.Empty<byte>(), "Archive exceeds the 100 MB response limit."));
                continue;
            }
            total += content.Length;
            candidates.Add(new(entry.FullName, content));
        }
        return candidates;
    }

    private static byte[] ReadLimited(Stream stream, long limit)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            if (output.Length > limit - read) throw new InvalidDataException("Response exceeds the 10 MB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static Dictionary<string, List<(string Path, byte[] Content)>> ExistingContent(string root)
    {
        var map = new Dictionary<string, List<(string, byte[])>>(StringComparer.Ordinal);
        foreach (string file in EnumerateLibraryFiles(root))
        {
            byte[] content;
            try { content = File.ReadAllBytes(file); } catch { continue; }
            if (!ResponseFile.Validate(content, out _)) continue;
            string hash = Convert.ToHexString(SHA256.HashData(content));
            if (!map.TryGetValue(hash, out var list)) map[hash] = list = new();
            list.Add((file, content));
        }
        return map;
    }

    private static string? FindExact(Dictionary<string, List<(string Path, byte[] Content)>> existing,
        byte[] hash, byte[] content) =>
        existing.TryGetValue(Convert.ToHexString(hash), out var matches)
            ? matches.FirstOrDefault(m => m.Content.AsSpan().SequenceEqual(content)).Path
            : null;

    private static IEnumerable<string> EnumerateLibraryFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            IEnumerable<string> files;
            IEnumerable<string> dirs;
            try { files = Directory.EnumerateFiles(dir, "*.json").ToArray(); dirs = Directory.EnumerateDirectories(dir).ToArray(); }
            catch { continue; }
            foreach (string file in files)
                if (!Path.GetFileName(file).Equals(FolderSidecar, StringComparison.OrdinalIgnoreCase)
                    && !IsReparsePoint(file)) yield return file;
            foreach (string child in dirs)
                if (!IsReparsePoint(child)) pending.Push(child);
        }
    }

    private static bool TryDestination(string root, string category, string entry, out string relative,
        out string full, out string? error)
    {
        relative = JoinPath(category, entry);
        full = root;
        error = null;
        if (!ValidRelative(category, allowEmpty: true, out error) || !ValidRelative(entry, allowEmpty: false, out error))
            return false;
        full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(root, full)) { error = "Destination escapes the response library."; return false; }
        if (HasReparsePoint(root, full)) { error = "Destination crosses a link or junction."; return false; }
        return true;
    }

    private static bool ValidRelative(string value, bool allowEmpty, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(value))
        {
            if (allowEmpty) return true;
            error = "Destination path is empty.";
            return false;
        }
        if (Path.IsPathRooted(value) || value.StartsWith('/') || value.StartsWith('\\'))
        { error = "Rooted paths are not allowed."; return false; }

        foreach (string part in value.Replace('\\', '/').Split('/', StringSplitOptions.None))
        {
            string stem = Path.GetFileNameWithoutExtension(part).ToUpperInvariant();
            bool reserved = stem is "CON" or "PRN" or "AUX" or "NUL"
                || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT"))
                    && stem[3] is >= '1' and <= '9';
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.Contains(':') || reserved)
            { error = $"Unsafe path segment: {part}"; return false; }
        }
        return true;
    }

    private static string JoinPath(string category, string entry) =>
        string.IsNullOrEmpty(category) ? entry.Replace('\\', '/')
            : category.Replace('\\', '/').TrimEnd('/') + "/" + entry.Replace('\\', '/');

    private static bool IsInside(string root, string full)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(full));
        return !Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool HasReparsePoint(string root, string destination)
    {
        string relative = Path.GetRelativePath(root, destination);
        string current = root;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((Directory.Exists(current) || File.Exists(current)) && IsReparsePoint(current)) return true;
        }
        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }

    private static byte[] HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private static string NextFree(string destination, HashSet<string> reserved)
    {
        string dir = Path.GetDirectoryName(destination)!;
        string name = Path.GetFileNameWithoutExtension(destination);
        string ext = Path.GetExtension(destination);
        string candidate = Path.Combine(dir, $"{name} (imported){ext}");
        for (int n = 2; File.Exists(candidate) || reserved.Contains(candidate); n++)
            candidate = Path.Combine(dir, $"{name} (imported {n}){ext}");
        return candidate;
    }

    private static void ZipDirectory(string sourceDir, string destZip)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destZip))!);
        if (File.Exists(destZip)) File.Delete(destZip);
        ZipFile.CreateFromDirectory(sourceDir, destZip, CompressionLevel.Optimal, includeBaseDirectory: false);
    }
}
