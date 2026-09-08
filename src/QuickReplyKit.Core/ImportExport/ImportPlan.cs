namespace QuickReplyKit.Core.ImportExport;

public enum ImportClassification { New, DuplicateElsewhere, Identical, Conflict, Invalid, Unsafe }

public enum ImportAction { Import, Skip, Overwrite, KeepBoth }

public enum ImportItemKind { Response, Script }

public enum ImportOutcomeKind { Imported, Skipped, Overwritten, KeptBoth, Invalid, Unsafe, Stale, Failed }

/// <summary>One immutable row in a validated import preview.</summary>
public sealed class ImportPlanEntry
{
    internal ImportPlanEntry(int id, ImportItemKind kind, string sourcePath, string destinationPath,
        ImportClassification classification, ImportAction defaultAction, string? matchingExistingPath,
        string? message, string? codeSource, byte[] content, string? destinationFullPath, byte[]? destinationHash,
        string? keepBothFullPath, string? scriptLanguage = null)
    {
        Id = id;
        Kind = kind;
        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        Classification = classification;
        DefaultAction = defaultAction;
        MatchingExistingPath = matchingExistingPath;
        Message = message;
        CodeSource = codeSource;
        ScriptLanguage = scriptLanguage;
        Content = content;
        DestinationFullPath = destinationFullPath;
        DestinationHash = destinationHash;
        KeepBothFullPath = keepBothFullPath;
    }

    public int Id { get; }
    public ImportItemKind Kind { get; }
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public ImportClassification Classification { get; }
    public ImportAction DefaultAction { get; }
    public string? MatchingExistingPath { get; }
    public string? Message { get; }
    public string? CodeSource { get; }
    public string? ScriptLanguage { get; }

    internal byte[] Content { get; }
    internal string? DestinationFullPath { get; }
    internal byte[]? DestinationHash { get; }
    internal string? KeepBothFullPath { get; }
}

/// <summary>The exact source snapshot and validated rows shown by the preview.</summary>
public sealed class ImportPlan
{
    internal ImportPlan(string libraryRoot, string sourcePath, byte[] sourceHash,
        IEnumerable<ImportPlanEntry> entries)
    {
        LibraryRoot = libraryRoot;
        SourcePath = sourcePath;
        SourceHash = sourceHash;
        Entries = Array.AsReadOnly(entries.ToArray());
    }

    public string LibraryRoot { get; }
    public string SourcePath { get; }
    public IReadOnlyList<ImportPlanEntry> Entries { get; }
    internal byte[] SourceHash { get; }
}

public sealed record ImportOutcome(int EntryId, string DestinationPath, ImportOutcomeKind Kind, string Message);
