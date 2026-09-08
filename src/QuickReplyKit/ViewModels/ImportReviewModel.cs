using QuickReplyKit.Core.ImportExport;
using QuickReplyKit.Core.Scripts;

namespace QuickReplyKit.ViewModels;

internal sealed record ImportConflictChoice(string Label, ImportAction Action);
internal sealed record ImportReviewDecision(IReadOnlyDictionary<int, ImportAction> Actions,
    IReadOnlySet<int> AcceptedScriptEntries);

internal sealed class ImportReviewRow : ViewModelBase
{
    private static readonly IReadOnlyList<ImportConflictChoice> Choices =
    [
        new("Overwrite", ImportAction.Overwrite),
        new("Keep both", ImportAction.KeepBoth),
    ];

    private bool _isSelected;
    private ImportAction _conflictAction;

    internal ImportReviewRow(ImportPlanEntry entry, bool canBulkSelect)
    {
        Entry = entry;
        CanSelect = entry.Classification is ImportClassification.New
            or ImportClassification.DuplicateElsewhere or ImportClassification.Conflict;
        CanBulkSelect = CanSelect && entry.Kind == ImportItemKind.Response && canBulkSelect;
        _isSelected = CanBulkSelect && entry.DefaultAction != ImportAction.Skip;
        _conflictAction = entry.DefaultAction == ImportAction.Overwrite
            ? ImportAction.Overwrite : ImportAction.KeepBoth;
    }

    internal event EventHandler? SelectionChanged;
    internal ImportPlanEntry Entry { get; }
    public ImportClassification Classification => Entry.Classification;
    public ImportItemKind Kind => Entry.Kind;
    public bool IsScript => Kind == ImportItemKind.Script;
    public bool IsNativeScript => IsScript &&
        string.Equals(Entry.ScriptLanguage, ScriptDefinitionFile.NativeLanguage, StringComparison.Ordinal);
    public bool IsRestrictedScript => IsScript &&
        string.Equals(Entry.ScriptLanguage, ScriptDefinitionFile.Language, StringComparison.Ordinal);
    public string KindLabel => IsScript ? "Script" : "Response";
    public string? CodeSource => Entry.CodeSource;
    public string? ScriptLanguage => Entry.ScriptLanguage;
    public string LanguageLabel => IsNativeScript
        ? "Full-trust Windows PowerShell · powershell-1"
        : IsRestrictedScript ? "Restricted QuickReplyKit expression · qrk-expr-1"
        : IsScript ? "Invalid script definition" : "";
    public string ScriptWarning => IsNativeScript
        ? "Full-trust Windows PowerShell runs as your signed-in Windows user. It can read, change, or delete files; access the network, registry, environment, and credentials; and start processes. Only accept code you have reviewed and trust."
        : IsRestrictedScript
            ? "Restricted QuickReplyKit expression is limited to built-in text and date functions. It cannot access files, the network, registry, environment, credentials, or start processes."
            : IsScript ? "This script definition is invalid and cannot be accepted." : "";
    public string SourceAutomationName => IsNativeScript
        ? "Exact full-trust Windows PowerShell source"
        : IsRestrictedScript ? "Exact restricted QuickReplyKit expression source"
        : "Invalid script source";
    public string DestinationPath => Entry.DestinationPath;
    public string Status => Classification switch
    {
        ImportClassification.DuplicateElsewhere => "Duplicate",
        ImportClassification.Identical => "Identical",
        ImportClassification.Conflict => "Changed",
        ImportClassification.Invalid => "Invalid",
        ImportClassification.Unsafe => "Unsafe",
        _ => "New",
    };
    public string Detail => Entry.Message ?? Classification switch
    {
        ImportClassification.DuplicateElsewhere => $"Same content exists at {Entry.MatchingExistingPath}",
        ImportClassification.Identical => "Already up to date",
        ImportClassification.Conflict => $"A {KindLabel.ToLowerInvariant()} already exists at this path",
        _ => "",
    };
    public string SelectionName => IsScript
        ? IsNativeScript
            ? $"Accept full-trust Windows PowerShell script {DestinationPath} after reviewing its exact source"
            : $"Accept restricted QuickReplyKit expression {DestinationPath} after reviewing its exact source"
        : $"Select {DestinationPath}";
    public string ConflictActionName => $"Conflict action for {DestinationPath}";
    public bool CanSelect { get; }
    public bool CanBulkSelect { get; }
    public bool IsConflict => Classification == ImportClassification.Conflict;
    public bool IsConflictChoiceEnabled => IsConflict && IsSelected;
    public IReadOnlyList<ImportConflictChoice> ConflictChoices => Choices;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!CanSelect || !Set(ref _isSelected, value)) return;
            OnPropertyChanged(nameof(IsConflictChoiceEnabled));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ImportAction ConflictAction
    {
        get => _conflictAction;
        set => Set(ref _conflictAction,
            value == ImportAction.KeepBoth ? ImportAction.KeepBoth : ImportAction.Overwrite);
    }

    internal ImportAction Decision => !IsSelected ? ImportAction.Skip
        : IsConflict ? ConflictAction : ImportAction.Import;
}

internal sealed class ImportReviewModel : ViewModelBase
{
    private bool _updating;

    internal ImportReviewModel(ImportPlan plan, Func<ImportPlanEntry, bool>? canBulkSelect = null)
    {
        Rows = plan.Entries.Select(entry =>
            new ImportReviewRow(entry, canBulkSelect?.Invoke(entry) ?? true)).ToArray();
        foreach (var row in Rows) row.SelectionChanged += Row_SelectionChanged;
    }

    public IReadOnlyList<ImportReviewRow> Rows { get; }
    public int SelectedCount => Rows.Count(row => row.IsSelected);
    public int SelectableCount => Rows.Count(row => row.CanSelect);
    public string SelectedSummary => $"{SelectedCount} of {SelectableCount} items selected";
    public bool CanImport => SelectedCount > 0;
    public IReadOnlyDictionary<int, ImportAction> Decisions =>
        Rows.ToDictionary(row => row.Entry.Id, row => row.Decision);
    public IReadOnlySet<int> AcceptedScriptEntries => Rows
        .Where(row => row.IsScript && row.IsSelected)
        .Select(row => row.Entry.Id).ToHashSet();

    public ImportReviewDecision Decision => new(Decisions, AcceptedScriptEntries);

    public void SelectAll() => SetSelection(row => row.CanBulkSelect, true);
    public void Clear() => SetSelection(row => row.CanSelect, false);

    private void SetSelection(Func<ImportReviewRow, bool> predicate, bool selected)
    {
        _updating = true;
        foreach (var row in Rows.Where(predicate)) row.IsSelected = selected;
        _updating = false;
        NotifySelection();
    }

    private void Row_SelectionChanged(object? sender, EventArgs e)
    {
        if (!_updating) NotifySelection();
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSummary));
        OnPropertyChanged(nameof(CanImport));
    }
}
