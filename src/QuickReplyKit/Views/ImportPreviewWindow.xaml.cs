using System.Windows;
using System.Windows.Media;
using QuickReplyKit.Core.ImportExport;

namespace QuickReplyKit.Views;

/// <summary>Reviews and edits the exact import plan that will be executed.</summary>
public partial class ImportPreviewWindow : Window
{
    private sealed record Choice(string Label, ImportAction Action);

    private sealed class Row
    {
        public required ImportPlanEntry Entry { get; init; }
        public required string Kind { get; init; }
        public required System.Windows.Media.Brush Badge { get; init; }
        public required IReadOnlyList<Choice> Choices { get; init; }
        public ImportAction Action { get; set; }
        public bool CanChoose => Choices.Count > 1;
        public string Dest => Entry.DestinationPath;
        public string Detail => Entry.Message ?? (Entry.MatchingExistingPath is { } match
            ? $"Same content already exists at {match}" : "");
    }

    private readonly List<Row> _rows;
    public IReadOnlyDictionary<int, ImportAction> Decisions =>
        _rows.ToDictionary(r => r.Entry.Id, r => r.Action);

    private ImportPreviewWindow(ImportPlan plan)
    {
        InitializeComponent();
        _rows = plan.Entries.Select(MakeRow).ToList();
        EntryList.ItemsSource = _rows;

        string Count(ImportClassification kind) => _rows.Count(r => r.Entry.Classification == kind).ToString();
        Summary.Text = $"{Count(ImportClassification.New)} new · "
            + $"{Count(ImportClassification.Conflict)} changed · "
            + $"{Count(ImportClassification.DuplicateElsewhere)} duplicate · "
            + $"{Count(ImportClassification.Identical)} identical · "
            + $"{_rows.Count(r => r.Entry.Classification is ImportClassification.Invalid or ImportClassification.Unsafe)} skipped";
        ConflictPanel.Visibility = _rows.Any(r => r.Entry.Classification == ImportClassification.Conflict)
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateImportButton();
    }

    public static IReadOnlyDictionary<int, ImportAction>? Prompt(Window owner, ImportPlan plan)
    {
        var dialog = new ImportPreviewWindow(plan) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Decisions : null;
    }

    private static Row MakeRow(ImportPlanEntry entry)
    {
        var choices = entry.Classification switch
        {
            ImportClassification.New or ImportClassification.DuplicateElsewhere => new[]
            {
                new Choice("Import", ImportAction.Import), new Choice("Skip", ImportAction.Skip),
            },
            ImportClassification.Conflict => new[]
            {
                new Choice("Skip", ImportAction.Skip), new Choice("Overwrite", ImportAction.Overwrite),
                new Choice("Keep both", ImportAction.KeepBoth),
            },
            _ => new[] { new Choice("Skip", ImportAction.Skip) },
        };
        return new Row
        {
            Entry = entry,
            Kind = entry.Classification switch
            {
                ImportClassification.DuplicateElsewhere => "DUPLICATE",
                ImportClassification.Identical => "IDENTICAL",
                ImportClassification.Conflict => "CHANGED",
                ImportClassification.Invalid => "INVALID",
                ImportClassification.Unsafe => "UNSAFE",
                _ => "NEW",
            },
            Badge = Hex(entry.Classification switch
            {
                ImportClassification.New => "#BBF7D0",
                ImportClassification.Conflict => "#FDE68A",
                ImportClassification.Invalid or ImportClassification.Unsafe => "#FECACA",
                _ => "#E5E7EB",
            }),
            Choices = choices,
            Action = entry.DefaultAction,
        };
    }

    private void ApplyConflicts_Click(object sender, RoutedEventArgs e)
    {
        ImportAction action = PolicyOverwrite.IsChecked == true ? ImportAction.Overwrite
            : PolicyKeepBoth.IsChecked == true ? ImportAction.KeepBoth : ImportAction.Skip;
        foreach (var row in _rows.Where(r => r.Entry.Classification == ImportClassification.Conflict))
            row.Action = action;
        EntryList.Items.Refresh();
        UpdateImportButton();
    }

    private void Action_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        UpdateImportButton();

    private void UpdateImportButton()
    {
        if (ImportButton is not null) ImportButton.IsEnabled = _rows.Any(r => r.Action != ImportAction.Skip);
    }

    private void Import_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private static SolidColorBrush Hex(string hex)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)
            System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
