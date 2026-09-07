using QuickReplyKit.Core.ImportExport;

namespace QuickReplyKit.ViewModels;

internal sealed record ImportConflictChoice(string Label, ImportAction Action);

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
        CanBulkSelect = CanSelect && canBulkSelect;
        _isSelected = CanBulkSelect && entry.DefaultAction != ImportAction.Skip;
        _conflictAction = entry.DefaultAction == ImportAction.Overwrite
            ? ImportAction.Overwrite : ImportAction.KeepBoth;
    }

    internal event EventHandler? SelectionChanged;
    internal ImportPlanEntry Entry { get; }
    public ImportClassification Classification => Entry.Classification;
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
        ImportClassification.Conflict => "A response already exists at this path",
        _ => "",
    };
    public string SelectionName => $"Select {DestinationPath}";
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
    public string SelectedSummary => $"{SelectedCount} of {SelectableCount} responses selected";
    public bool CanImport => SelectedCount > 0;
    public IReadOnlyDictionary<int, ImportAction> Decisions =>
        Rows.ToDictionary(row => row.Entry.Id, row => row.Decision);

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
