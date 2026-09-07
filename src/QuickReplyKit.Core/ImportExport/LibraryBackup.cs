using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.Storage;

namespace QuickReplyKit.Core.ImportExport;

/// <summary>One saved library snapshot.</summary>
public sealed record BackupInfo(string Path, DateTime Created, long Bytes);

/// <summary>Rolling, self-contained library/config snapshots with rollback-safe restore.</summary>
public static class LibraryBackup
{
    public const int MaxKeep = 10;
    public const long MaxTotalBytes = 500L * 1024 * 1024;
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(90);
    internal const int MaxArchiveEntries = 10_000;
    internal const long MaxArchiveEntryBytes = 10L * 1024 * 1024;
    internal const long MaxArchiveExpandedBytes = 500L * 1024 * 1024;

    private const int SnapshotSchema = 2;
    private const string Prefix = "qrk-backup-";
    private const string ManifestEntry = "quickreplykit-backup.json";
    private const string LibraryPrefix = "library/";
    private const string ConfigEntry = "config.json";

    private sealed record Manifest(int Schema, bool ConfigIncluded);
    private sealed record StagedSnapshot(string Library, string? Config);

    /// <summary>
    /// Snapshot the library and current app config. Returns <c>null</c> when the library is missing
    /// or empty, because there is no user content to protect.
    /// </summary>
    public static string? Create(string libraryRoot, string backupsDir, string? reason = null) =>
        Create(libraryRoot, backupsDir, AppPaths.ConfigPath, reason);

    internal static string? Create(string libraryRoot, string backupsDir, string configPath, string? reason)
    {
        backupsDir = BackupLocation.EnsureWritable(backupsDir, libraryRoot);
        if (!Directory.Exists(libraryRoot) || !Directory.EnumerateFileSystemEntries(libraryRoot).Any())
            return null;

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string dest = Path.Combine(backupsDir, $"{Prefix}{stamp}.zip");
        for (int n = 2; File.Exists(dest); n++)
            dest = Path.Combine(backupsDir, $"{Prefix}{stamp}-{n}.zip");

        string temp = dest + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                foreach (var dir in Directory.EnumerateDirectories(libraryRoot, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(libraryRoot, dir).Replace('\\', '/');
                    zip.CreateEntry(LibraryPrefix + rel + "/");
                }
                foreach (var file in Directory.EnumerateFiles(libraryRoot, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(libraryRoot, file).Replace('\\', '/');
                    zip.CreateEntryFromFile(file, LibraryPrefix + rel, CompressionLevel.Optimal);
                }

                bool hasConfig = File.Exists(configPath);
                if (hasConfig) zip.CreateEntryFromFile(configPath, ConfigEntry, CompressionLevel.Optimal);
                using var writer = new StreamWriter(zip.CreateEntry(ManifestEntry).Open());
                writer.Write(JsonSerializer.Serialize(new Manifest(SnapshotSchema, hasConfig)));
            }
            File.Move(temp, dest);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }

        Prune(backupsDir);
        Diag.Info($"backup created: {Path.GetFileName(dest)}" + (reason is null ? "" : $" ({reason})"));
        return dest;
    }

    /// <summary>
    /// Create a mandatory safety snapshot. Failures are wrapped with a caller-safe message so a
    /// destructive operation can stop before changing user data.
    /// </summary>
    public static string? CreateRequired(string libraryRoot, string backupsDir, string reason) =>
        CreateRequired(libraryRoot, backupsDir, AppPaths.ConfigPath, reason);

    internal static string? CreateRequired(string libraryRoot, string backupsDir, string configPath, string reason)
    {
        try { return Create(libraryRoot, backupsDir, configPath, reason); }
        catch (Exception ex)
        {
            throw new IOException(
                "The required safety backup could not be created. No changes were made. " +
                "Check that the backup folder is available and writable.", ex);
        }
    }

    /// <summary>Saved snapshots, newest first.</summary>
    public static IReadOnlyList<BackupInfo> List(string backupsDir)
    {
        backupsDir = BackupLocation.EnsureWritable(backupsDir);
        return Directory.EnumerateFiles(backupsDir, Prefix + "*.zip")
            .Select(p => { var fi = new FileInfo(p); return new BackupInfo(p, fi.LastWriteTime, fi.Length); })
            .OrderByDescending(b => b.Created).ThenByDescending(b => b.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Restore a snapshot over the library. The selected archive is protected and fully staged
    /// before retention can prune it; replacement uses a same-parent move and restores the exact
    /// previous library if installation or config replacement fails. Legacy library-only ZIPs are
    /// accepted and leave the current config unchanged.
    /// </summary>
    public static bool Restore(string backupZip, string libraryRoot, string backupsDir) =>
        Restore(backupZip, libraryRoot, backupsDir, AppPaths.ConfigPath, null);

    internal static bool Restore(string backupZip, string libraryRoot, string backupsDir, string configPath,
        Action<string, string>? installLibrary, int maxEntries = MaxArchiveEntries,
        long maxEntryBytes = MaxArchiveEntryBytes, long maxExpandedBytes = MaxArchiveExpandedBytes)
    {
        backupsDir = BackupLocation.EnsureWritable(backupsDir, libraryRoot);
        if (!File.Exists(backupZip)) throw new FileNotFoundException("Backup not found", backupZip);

        string libraryFull = Path.GetFullPath(libraryRoot);
        string parent = Path.GetDirectoryName(libraryFull)
            ?? throw new IOException("The library folder has no usable parent folder.");
        Directory.CreateDirectory(parent);
        string work = Path.Combine(parent, ".qrk-restore-" + Guid.NewGuid().ToString("N"));
        string protectedZip = Path.Combine(work, "selected.zip");
        string rollback = Path.Combine(work, "previous-library");
        bool hadLibrary = Directory.Exists(libraryFull);
        bool preserveRecovery = false;

        Directory.CreateDirectory(work);
        try
        {
            File.Copy(backupZip, protectedZip); // before CreateRequired can prune backupZip
            var staged = ExtractAndValidate(protectedZip, work, maxEntries, maxEntryBytes, maxExpandedBytes);
            if (staged.Config is not null) RetargetConfig(staged.Config, libraryFull);
            CreateRequired(libraryFull, backupsDir, configPath, "pre-restore safety");

            try
            {
                if (hadLibrary) Directory.Move(libraryFull, rollback);
                (installLibrary ?? Directory.Move)(staged.Library, libraryFull);
                if (!Directory.Exists(libraryFull))
                    throw new IOException("The restored library was not installed.");
                if (staged.Config is not null) ReplaceFile(staged.Config, configPath);
            }
            catch (Exception restoreError)
            {
                try
                {
                    if (Directory.Exists(libraryFull)) Directory.Delete(libraryFull, recursive: true);
                    if (hadLibrary && Directory.Exists(rollback)) Directory.Move(rollback, libraryFull);
                }
                catch (Exception rollbackError)
                {
                    preserveRecovery = true;
                    throw new IOException(
                        $"Restore failed and automatic rollback could not finish. The original library remains at '{rollback}'.",
                        new AggregateException(restoreError, rollbackError));
                }
                throw new IOException("Restore failed. The original library was restored and no changes were kept.",
                    restoreError);
            }

            try { if (Directory.Exists(rollback)) Directory.Delete(rollback, recursive: true); } catch { /* old copy */ }
            Diag.Info($"library restored from {Path.GetFileName(backupZip)}");
            return staged.Config is not null;
        }
        finally
        {
            try { if (!preserveRecovery && Directory.Exists(work)) Directory.Delete(work, recursive: true); }
            catch { /* recovery evidence */ }
        }
    }

    private static StagedSnapshot ExtractAndValidate(string archivePath, string work, int maxEntries,
        long maxEntryBytes, long maxExpandedBytes)
    {
        string library = Path.Combine(work, "staged-library");
        string config = Path.Combine(work, ConfigEntry);
        Directory.CreateDirectory(library);

        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count > maxEntries)
            throw new InvalidDataException($"Backup contains more than {maxEntries:N0} entries.");
        long declaredBytes = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.Length > maxEntryBytes)
                throw new InvalidDataException($"Backup entry is too large: {entry.FullName}");
            declaredBytes = checked(declaredBytes + entry.Length);
            if (declaredBytes > maxExpandedBytes)
                throw new InvalidDataException("Backup expands beyond the allowed size.");
        }

        long extractedBytes = 0;
        var manifests = zip.Entries.Where(e => e.FullName.Equals(ManifestEntry, StringComparison.Ordinal)).ToList();
        bool modern = manifests.Count > 0;
        bool wantsConfig = false;
        if (modern)
        {
            if (manifests.Count != 1) throw new InvalidDataException("Backup contains duplicate manifests.");
            using var manifestBytes = new MemoryStream();
            CopyBounded(manifests[0], manifestBytes, ref extractedBytes, maxEntryBytes, maxExpandedBytes);
            manifestBytes.Position = 0;
            var manifest = JsonSerializer.Deserialize<Manifest>(manifestBytes)
                ?? throw new InvalidDataException("Backup manifest is invalid.");
            if (manifest.Schema != SnapshotSchema)
                throw new InvalidDataException($"Backup schema {manifest.Schema} is not supported.");
            wantsConfig = manifest.ConfigIncluded;
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool configFound = false;
        int libraryFiles = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.Equals(ManifestEntry, StringComparison.Ordinal)) continue;

            string relative;
            string root;
            if (modern && entry.FullName.Equals(ConfigEntry, StringComparison.Ordinal))
            {
                relative = ConfigEntry;
                root = work;
                configFound = true;
            }
            else if (modern && entry.FullName.StartsWith(LibraryPrefix, StringComparison.Ordinal))
            {
                relative = entry.FullName[LibraryPrefix.Length..];
                root = library;
                if (entry.Name.Length > 0) libraryFiles++;
            }
            else if (!modern)
            {
                relative = entry.FullName;
                root = library;
                if (entry.Name.Length > 0) libraryFiles++;
            }
            else
            {
                throw new InvalidDataException($"Backup contains an unexpected entry: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(relative)) continue;
            string destination = SafePath(root, relative);
            if (!paths.Add(Path.GetFullPath(destination)))
                throw new InvalidDataException($"Backup contains a duplicate path: {entry.FullName}");
            if (entry.Name.Length == 0) Directory.CreateDirectory(destination);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var output = File.Create(destination);
                CopyBounded(entry, output, ref extractedBytes, maxEntryBytes, maxExpandedBytes);
            }
        }

        if (libraryFiles == 0) throw new InvalidDataException("Backup contains no library files.");
        if (modern && wantsConfig != configFound)
            throw new InvalidDataException("Backup manifest and config content do not agree.");
        return new StagedSnapshot(library, configFound ? config : null);
    }

    private static void CopyBounded(ZipArchiveEntry entry, Stream output, ref long total,
        long maxEntryBytes, long maxExpandedBytes)
    {
        using var input = entry.Open();
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            written += read;
            total += read;
            if (written > maxEntryBytes || total > maxExpandedBytes)
                throw new InvalidDataException($"Backup entry expands beyond the allowed size: {entry.FullName}");
            output.Write(buffer, 0, read);
        }
    }

    private static void RetargetConfig(string configPath, string libraryRoot)
    {
        JsonObject config;
        try
        {
            config = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject
                ?? throw new InvalidDataException("Backup config is not a JSON object.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Backup config is invalid.", ex); }
        config["libraryPath"] = libraryRoot;
        File.WriteAllText(configPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string SafePath(string root, string relative)
    {
        string rootFull = Path.GetFullPath(root);
        string full = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        string rel = Path.GetRelativePath(rootFull, full);
        if (Path.IsPathRooted(rel) || rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"Backup entry escapes its snapshot root: {relative}");
        return full;
    }

    private static void ReplaceFile(string source, string destination)
    {
        string parent = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        Directory.CreateDirectory(parent);
        string temp = Path.Combine(parent, ".qrk-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.Copy(source, temp);
            if (File.Exists(destination)) File.Replace(temp, destination, null, ignoreMetadataErrors: true);
            else File.Move(temp, destination);
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ } }
    }

    private static void Prune(string backupsDir) =>
        Prune(backupsDir, MaxKeep, MaxTotalBytes, MaxAge, DateTime.Now);

    internal static void Prune(string backupsDir, int maxKeep, long maxBytes, TimeSpan maxAge, DateTime now)
    {
        long bytes = 0;
        int kept = 0;
        foreach (var backup in List(backupsDir))
        {
            bool overAge = kept > 0 && now - backup.Created > maxAge;
            bool overCount = kept >= maxKeep;
            bool overSize = kept > 0 && backup.Bytes > maxBytes - bytes;
            if (overAge || overCount || overSize)
            {
                try { File.Delete(backup.Path); } catch { /* best effort */ }
            }
            else
            {
                kept++;
                bytes += backup.Bytes;
            }
        }
    }
}
