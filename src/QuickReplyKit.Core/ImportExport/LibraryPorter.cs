using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Scripts;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Core.ImportExport;

public enum ConflictPolicy { Overwrite, KeepBoth, Skip }

public sealed class ImportResult
{
    public int Imported { get; internal set; }
    public int SkippedInvalid { get; internal set; }
    public int Conflicts { get; internal set; }
    public List<ImportOutcome> Outcomes { get; } = new();
}

/// <summary>Validated import/export for a single response or response ZIP.</summary>
public static class LibraryPorter
{
    public const int MaxImportEntries = 10_000;
    public const long MaxEntryBytes = 10L * 1024 * 1024;
    public const long MaxImportBytes = 100L * 1024 * 1024;
    private const string FolderSidecar = ".qrkfolder.json";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    private sealed record Candidate(ImportItemKind Kind, string Path, byte[] Content, string? Error = null);

    public static void Export(ResponseItem item, string destPath)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Diag.Verbose("LibraryPorter", "Export", "start kind=response");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destPath))!);
            File.Copy(item.FullPath, destPath, overwrite: true);
            Diag.Info("LibraryPorter", "Export",
                $"complete kind=response entries=1 bytes={new FileInfo(destPath).Length} elapsedMs={sw.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            Diag.Warn("LibraryPorter", "Export", "failed kind=response", ex);
            throw;
        }
    }

    public static void ExportFolder(string libraryRoot, string categoryPath, string destZip)
    {
        string source = string.IsNullOrEmpty(categoryPath)
            ? libraryRoot
            : Path.Combine(libraryRoot, categoryPath.Replace('/', Path.DirectorySeparatorChar));
        ExportZip("folder", source, destZip);
    }

    public static void ExportLibrary(string libraryRoot, string destZip) => ExportZip("library", libraryRoot, destZip);

    /// <summary>Build the exact validated plan that the preview displays and the importer executes.</summary>
    public static ImportPlan PlanImport(string libraryRoot, string sourcePath, string targetCategory)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Diag.Verbose("LibraryPorter", "ImportPlan", "start");
        try
        {
            var plan = BuildImportPlan(libraryRoot, sourcePath, targetCategory);
            int Count(ImportClassification kind) => plan.Entries.Count(e => e.Classification == kind);
            Diag.Info("LibraryPorter", "ImportPlan",
                $"complete entries={plan.Entries.Count} new={Count(ImportClassification.New)} duplicate={Count(ImportClassification.DuplicateElsewhere)} identical={Count(ImportClassification.Identical)} conflicts={Count(ImportClassification.Conflict)} invalid={Count(ImportClassification.Invalid)} unsafe={Count(ImportClassification.Unsafe)} elapsedMs={sw.ElapsedMilliseconds}");
            return plan;
        }
        catch (Exception ex)
        {
            Diag.Warn("LibraryPorter", "ImportPlan", "failed", ex);
            throw;
        }
    }

    private static ImportPlan BuildImportPlan(string libraryRoot, string sourcePath, string targetCategory)
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
                entries.Add(Entry(id, candidate.Kind, candidate.Path, display, ImportClassification.Unsafe,
                    pathError ?? "Unsafe destination path."));
                continue;
            }
            if (!reserved.Add(full))
            {
                entries.Add(Entry(id, candidate.Kind, candidate.Path, relative, ImportClassification.Unsafe,
                    "Another incoming entry resolves to the same destination."));
                continue;
            }
            if (candidate.Error is not null)
            {
                entries.Add(Entry(id, candidate.Kind, candidate.Path, relative, ImportClassification.Invalid, candidate.Error));
                continue;
            }

            string? codeSource = null;
            string? scriptLanguage = null;
            string? validationError;
            if (candidate.Kind == ImportItemKind.Script)
            {
                try
                {
                    var parsed = ScriptDefinitionFile.Parse(Utf8.GetString(candidate.Content));
                    validationError = parsed.Success ? null : parsed.Error ?? parsed.Marker;
                    codeSource = parsed.Value?.Expression;
                    scriptLanguage = parsed.Value?.Language;
                }
                catch (DecoderFallbackException)
                {
                    validationError = "Script definition is not valid UTF-8.";
                }
            }
            else
            {
                if (ResponseFile.Validate(candidate.Content, out validationError)) validationError = null;
            }
            if (validationError is not null)
            {
                entries.Add(Entry(id, candidate.Kind, candidate.Path, relative, ImportClassification.Invalid,
                    validationError));
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
            else if (FindExact(existing, candidate.Kind, contentHash, candidate.Content) is { } duplicate)
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

            entries.Add(new ImportPlanEntry(id, candidate.Kind, candidate.Path, relative, classification, action,
                match, null, codeSource, candidate.Content, full, destinationHash, keepBoth, scriptLanguage));
        }

        return new ImportPlan(root, source, sourceHash, entries);
    }

    /// <summary>Execute the immutable preview plan with optional per-row decisions.</summary>
    public static ImportResult Import(ImportPlan plan, IReadOnlyDictionary<int, ImportAction>? decisions = null,
        IReadOnlySet<int>? acceptedScriptEntries = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Diag.Verbose("LibraryPorter", "ImportExecution", $"start entries={plan.Entries.Count}");
        try
        {
            ScriptTrustLedger? trust = null;
            string? trustError = null;
            if (acceptedScriptEntries is { Count: > 0 })
            {
                var opened = ScriptTrustLedger.Open();
                trust = opened.Success ? opened.Value : null;
                trustError = opened.Error ?? opened.Marker;
            }
            var result = ExecuteImport(plan, decisions, acceptedScriptEntries, trust, trustError);
            int Count(ImportOutcomeKind kind) => result.Outcomes.Count(o => o.Kind == kind);
            Diag.Info("LibraryPorter", "ImportExecution",
                $"complete entries={result.Outcomes.Count} imported={Count(ImportOutcomeKind.Imported)} overwritten={Count(ImportOutcomeKind.Overwritten)} keptBoth={Count(ImportOutcomeKind.KeptBoth)} skipped={Count(ImportOutcomeKind.Skipped)} invalid={Count(ImportOutcomeKind.Invalid)} unsafe={Count(ImportOutcomeKind.Unsafe)} stale={Count(ImportOutcomeKind.Stale)} failed={Count(ImportOutcomeKind.Failed)} elapsedMs={sw.ElapsedMilliseconds}");
            return result;
        }
        catch (Exception ex)
        {
            Diag.Warn("LibraryPorter", "ImportExecution", "failed", ex);
            throw;
        }
    }

    internal static ImportResult Import(ImportPlan plan, IReadOnlyDictionary<int, ImportAction>? decisions,
        IReadOnlySet<int>? acceptedScriptEntries, ScriptTrustLedger trust) =>
        ExecuteImport(plan, decisions, acceptedScriptEntries, trust, null);

    private static ImportResult ExecuteImport(ImportPlan plan, IReadOnlyDictionary<int, ImportAction>? decisions,
        IReadOnlySet<int>? acceptedScriptEntries, ScriptTrustLedger? trust, string? trustError)
    {
        var result = new ImportResult
        {
            Conflicts = plan.Entries.Count(e => e.Classification == ImportClassification.Conflict),
        };
        bool sourceStale = !SourceMatches(plan.SourcePath, plan.SourceHash);

        var actions = plan.Entries.ToDictionary(e => e.Id,
            e => NormalizeAction(e, decisions is not null && decisions.TryGetValue(e.Id, out var chosen)
                ? chosen : e.DefaultAction, acceptedScriptEntries));
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
                    entry.Kind == ImportItemKind.Script && acceptedScriptEntries?.Contains(entry.Id) != true
                        ? "Script was not explicitly accepted; skipped."
                        : entry.Classification == ImportClassification.Identical
                            ? "Identical; no changes made." : "Skipped.");
            else if (entry.Kind == ImportItemKind.Script && trust is null)
                outcome = new(entry.Id, entry.DestinationPath, ImportOutcomeKind.Failed,
                    "Script trust storage is unavailable; no file was written. " + trustError);
            else
                outcome = Write(plan.LibraryRoot, entry, actions[entry.Id], trust);

            result.Outcomes.Add(outcome);
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

    private static ImportPlanEntry Entry(int id, ImportItemKind kind, string source, string destination,
        ImportClassification classification, string message) =>
        new(id, kind, source, destination, classification, ImportAction.Skip, null, message, null,
            Array.Empty<byte>(), null, null, null);

    private static ImportAction NormalizeAction(ImportPlanEntry entry, ImportAction action,
        IReadOnlySet<int>? acceptedScriptEntries) =>
        entry.Kind == ImportItemKind.Script && acceptedScriptEntries?.Contains(entry.Id) != true
            ? ImportAction.Skip : entry.Classification switch
    {
        ImportClassification.New or ImportClassification.DuplicateElsewhere =>
            action == ImportAction.Import ? action : ImportAction.Skip,
        ImportClassification.Conflict => action is ImportAction.Overwrite or ImportAction.KeepBoth ? action : ImportAction.Skip,
        _ => ImportAction.Skip,
    };

    private static ImportOutcome Write(string root, ImportPlanEntry entry, ImportAction action,
        ScriptTrustLedger? trust)
    {
        string destination = action == ImportAction.KeepBoth ? entry.KeepBothFullPath! : entry.DestinationFullPath!;
        if (entry.Kind == ImportItemKind.Script)
            return WriteScript(root, entry, action, trust!, destination);

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
            string relative = Path.GetRelativePath(root, destination).Replace('\\', '/');
            return new(entry.Id, relative, kind, $"{kind}: {relative}");
        }
        catch (Exception ex)
        {
            return new(entry.Id, entry.DestinationPath, ImportOutcomeKind.Failed,
                $"Failed: {entry.DestinationPath} ({ex.Message})");
        }
    }

    private static ImportOutcome WriteScript(string root, ImportPlanEntry entry, ImportAction action,
        ScriptTrustLedger trust, string destination)
    {
        string temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        string? previous = null;
        bool committed = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(temp, entry.Content);
            if (File.Exists(destination))
            {
                previous = destination + ".rollback-" + Guid.NewGuid().ToString("N");
                File.Move(destination, previous);
            }
            File.Move(temp, destination);

            var installed = ScriptDefinitionFile.Load(destination);
            if (!installed.Success || !File.ReadAllBytes(destination).AsSpan().SequenceEqual(entry.Content))
                return new(entry.Id, entry.DestinationPath, ImportOutcomeKind.Failed,
                    "Installed script could not be verified; no library change was kept.");

            var accepted = trust.Trust(installed.Value!);
            if (!accepted.Success)
                return new(entry.Id, entry.DestinationPath, ImportOutcomeKind.Failed,
                    "Script could not be trusted; no library change was kept: " +
                    (accepted.Error ?? accepted.Marker));

            var kind = action switch
            {
                ImportAction.Overwrite => ImportOutcomeKind.Overwritten,
                ImportAction.KeepBoth => ImportOutcomeKind.KeptBoth,
                _ => ImportOutcomeKind.Imported,
            };
            string relative = Path.GetRelativePath(root, destination).Replace('\\', '/');
            committed = true;
            return new(entry.Id, relative, kind, $"{kind}: {relative}");
        }
        catch (Exception ex)
        {
            return new(entry.Id, entry.DestinationPath, ImportOutcomeKind.Failed,
                $"Failed: {entry.DestinationPath} ({ex.Message})");
        }
        finally
        {
            try
            {
                if (!committed)
                {
                    if (File.Exists(destination)) File.Delete(destination);
                    if (previous is not null && File.Exists(previous)) File.Move(previous, destination);
                }
                else if (previous is not null && File.Exists(previous)) File.Delete(previous);
            }
            catch { /* pre-import backup remains the final recovery boundary */ }
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }
    }

    private static bool DestinationMatches(string root, string path, byte[]? expectedHash)
    {
        try
        {
            if (!IsInside(root, path) || HasReparsePoint(root, path)) return false;
            if (expectedHash is null) return !File.Exists(path) && !Directory.Exists(path);
            return File.Exists(path) && expectedHash.AsSpan().SequenceEqual(HashFile(path));
        }
        catch { return false; }
    }

    private static bool SourceMatches(string path, byte[] expectedHash)
    {
        try { return File.Exists(path) && expectedHash.AsSpan().SequenceEqual(HashFile(path)); }
        catch { return false; }
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
            var kind = KindOf(source);
            long limit = EntryLimit(kind);
            long size = new FileInfo(source).Length;
            return new[] { size > limit
                ? new Candidate(kind, Path.GetFileName(source), Array.Empty<byte>(), LimitMessage(kind))
                : new Candidate(kind, Path.GetFileName(source), File.ReadAllBytes(source)) };
        }

        using var zip = ZipFile.OpenRead(source);
        if (zip.Entries.Count > MaxImportEntries)
            return new[] { new Candidate(ImportItemKind.Response, "Archive", Array.Empty<byte>(),
                $"Archive exceeds the {MaxImportEntries:N0}-entry limit.") };

        var responseEntries = zip.Entries.Where(entry => entry.Name.Length > 0
            && entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && !entry.Name.Equals(FolderSidecar, StringComparison.OrdinalIgnoreCase)).ToArray();
        long declaredTotal = 0;
        foreach (var entry in responseEntries)
        {
            if (declaredTotal > MaxImportBytes - entry.Length)
                return new[] { new Candidate(ImportItemKind.Response, "Archive", Array.Empty<byte>(),
                    "Archive exceeds the 100 MB response limit.") };
            declaredTotal += entry.Length;
        }

        var candidates = new List<Candidate>();
        long total = 0;
        foreach (var entry in responseEntries)
        {
            var kind = KindOf(entry.FullName);
            long limit = EntryLimit(kind);
            if (entry.Length > limit)
            {
                candidates.Add(new(kind, entry.FullName, Array.Empty<byte>(), LimitMessage(kind)));
                continue;
            }
            byte[] content;
            try
            {
                using var stream = entry.Open();
                content = ReadLimited(stream, limit, MaxImportBytes - total);
            }
            catch (InvalidDataException ex)
            {
                candidates.Add(new(KindOf(entry.FullName), entry.FullName, Array.Empty<byte>(), ex.Message));
                if (ex.Message.StartsWith("Archive exceeds", StringComparison.Ordinal)) break;
                continue;
            }
            total += content.Length;
            candidates.Add(new(KindOf(entry.FullName), entry.FullName, content));
        }
        return candidates;
    }

    private static byte[] ReadLimited(Stream stream, long entryLimit, long remainingTotal)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            if (output.Length > entryLimit - read)
                throw new InvalidDataException("Response exceeds the 10 MB limit.");
            if (output.Length > remainingTotal - read)
                throw new InvalidDataException("Archive exceeds the 100 MB response limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static Dictionary<(ImportItemKind Kind, string Hash), List<(string Path, byte[] Content)>> ExistingContent(string root)
    {
        var map = new Dictionary<(ImportItemKind, string), List<(string, byte[])>>();
        foreach (string file in EnumerateLibraryFiles(root))
        {
            byte[] content;
            try { content = File.ReadAllBytes(file); } catch { continue; }
            var kind = KindOf(file);
            bool valid;
            try
            {
                valid = kind == ImportItemKind.Script
                    ? ScriptDefinitionFile.Parse(Utf8.GetString(content)).Success
                    : ResponseFile.Validate(content, out _);
            }
            catch (DecoderFallbackException) { continue; }
            if (!valid) continue;
            var key = (kind, Convert.ToHexString(SHA256.HashData(content)));
            if (!map.TryGetValue(key, out var list)) map[key] = list = new();
            list.Add((file, content));
        }
        return map;
    }

    private static string? FindExact(
        Dictionary<(ImportItemKind Kind, string Hash), List<(string Path, byte[] Content)>> existing,
        ImportItemKind kind, byte[] hash, byte[] content) =>
        existing.TryGetValue((kind, Convert.ToHexString(hash)), out var matches)
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
            string stem = part.Split('.', 2)[0].ToUpperInvariant();
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
        if ((Directory.Exists(root) || File.Exists(root)) && IsReparsePoint(root)) return true;
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

    private static ImportItemKind KindOf(string path) =>
        path.EndsWith(ScriptDefinitionFile.Extension, StringComparison.OrdinalIgnoreCase)
            ? ImportItemKind.Script : ImportItemKind.Response;

    private static long EntryLimit(ImportItemKind kind) => kind == ImportItemKind.Script
        ? ScriptDefinitionFile.MaxFileBytes : MaxEntryBytes;

    private static string LimitMessage(ImportItemKind kind) => kind == ImportItemKind.Script
        ? "Script exceeds its storage limit." : "Response exceeds the 10 MB limit.";

    private static byte[] HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private static string NextFree(string destination, HashSet<string> reserved)
    {
        string dir = Path.GetDirectoryName(destination)!;
        bool script = destination.EndsWith(ScriptDefinitionFile.Extension, StringComparison.OrdinalIgnoreCase);
        string ext = script ? ScriptDefinitionFile.Extension : Path.GetExtension(destination);
        string name = Path.GetFileName(destination)[..^ext.Length];
        string candidate = Path.Combine(dir, $"{name} (imported){ext}");
        for (int n = 2; File.Exists(candidate) || reserved.Contains(candidate); n++)
            candidate = Path.Combine(dir, $"{name} (imported {n}){ext}");
        return candidate;
    }

    private static void ZipDirectory(string sourceDir, string destZip)
    {
        string source = Path.GetFullPath(sourceDir);
        string destination = Path.GetFullPath(destZip);
        if (IsInside(source, destination))
            throw new InvalidOperationException("Export destination must be outside the exported folder.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination)) File.Delete(destination);
        using var archive = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (string file in EnumerateExportFiles(source))
        {
            if (Path.GetFileName(file).Equals("script-trust.json", StringComparison.OrdinalIgnoreCase)) continue;
            archive.CreateEntryFromFile(file, Path.GetRelativePath(source, file).Replace('\\', '/'),
                CompressionLevel.Optimal);
        }
    }

    private static IEnumerable<string> EnumerateExportFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            string[] files;
            string[] dirs;
            try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (string file in files)
                if (!IsReparsePoint(file)) yield return file;
            foreach (string child in dirs)
                if (!IsReparsePoint(child)) pending.Push(child);
        }
    }

    private static void ExportZip(string kind, string sourceDir, string destZip)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Diag.Verbose("LibraryPorter", "Export", $"start kind={kind}");
        try
        {
            ZipDirectory(sourceDir, destZip);
            Diag.Info("LibraryPorter", "Export",
                $"complete kind={kind} bytes={new FileInfo(destZip).Length} elapsedMs={sw.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            Diag.Warn("LibraryPorter", "Export", $"failed kind={kind}", ex);
            throw;
        }
    }
}
