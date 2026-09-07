using System.Windows;
using QuickReplyKit.Core.ImportExport;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Views;

/// <summary>Reviews and edits the exact import plan that will be executed.</summary>
public partial class ImportPreviewWindow : Window
{
    private readonly ImportReviewModel _review;

    private ImportPreviewWindow(ImportPlan plan, Func<ImportPlanEntry, bool>? canBulkSelect = null)
    {
        InitializeComponent();
        _review = new ImportReviewModel(plan, canBulkSelect);
        DataContext = _review;

        string Count(ImportClassification kind) =>
            plan.Entries.Count(entry => entry.Classification == kind).ToString();
        Summary.Text = $"{Count(ImportClassification.New)} new · "
            + $"{Count(ImportClassification.Conflict)} changed · "
            + $"{Count(ImportClassification.DuplicateElsewhere)} duplicate · "
            + $"{Count(ImportClassification.Identical)} identical · "
            + $"{plan.Entries.Count(entry => entry.Classification is ImportClassification.Invalid or ImportClassification.Unsafe)} unavailable";
    }

    public static IReadOnlyDictionary<int, ImportAction>? Prompt(Window owner, ImportPlan plan,
        Func<ImportPlanEntry, bool>? canBulkSelect = null)
    {
        var dialog = new ImportPreviewWindow(plan, canBulkSelect) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._review.Decisions : null;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => _review.SelectAll();
    private void Clear_Click(object sender, RoutedEventArgs e) => _review.Clear();
    private void Import_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
