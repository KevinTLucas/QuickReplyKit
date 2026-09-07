using QuickReplyKit.Core.ImportExport;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Tests;

public class ImportReviewModelTests
{
    [Fact]
    public void Defaults_SelectOnlyEntriesWhosePlanImportsThem()
    {
        var model = Model(
            Entry(1, ImportClassification.New, ImportAction.Import),
            Entry(2, ImportClassification.DuplicateElsewhere, ImportAction.Skip),
            Entry(3, ImportClassification.Conflict, ImportAction.Skip),
            Entry(4, ImportClassification.Identical, ImportAction.Skip),
            Entry(5, ImportClassification.Invalid, ImportAction.Skip),
            Entry(6, ImportClassification.Unsafe, ImportAction.Skip));

        Assert.True(model.Rows[0].IsSelected);
        Assert.All(model.Rows.Skip(1), row => Assert.False(row.IsSelected));
        Assert.All(model.Rows.Skip(3), row => Assert.False(row.CanSelect));
        Assert.Equal(ImportAction.KeepBoth, model.Rows[2].ConflictAction);
        Assert.Equal("1 of 3 items selected", model.SelectedSummary);
        Assert.True(model.CanImport);
    }

    [Fact]
    public void SelectAll_SkipsExplicitApprovalRows_AndClearClearsEverything()
    {
        var plan = Plan(
            Entry(1, ImportClassification.New, ImportAction.Import),
            Entry(2, ImportClassification.DuplicateElsewhere, ImportAction.Skip),
            Entry(3, ImportClassification.Conflict, ImportAction.Skip));
        var model = new ImportReviewModel(plan, entry => entry.Id != 3);

        model.SelectAll();

        Assert.True(model.Rows[0].IsSelected);
        Assert.True(model.Rows[1].IsSelected);
        Assert.False(model.Rows[2].IsSelected);
        Assert.Equal("2 of 3 items selected", model.SelectedSummary);

        model.Rows[2].IsSelected = true; // explicit individual acceptance remains possible
        model.Clear();
        Assert.All(model.Rows, row => Assert.False(row.IsSelected));
        Assert.False(model.CanImport);
    }

    [Fact]
    public void Decisions_MapCheckboxesAndConflictChoice()
    {
        var model = Model(
            Entry(1, ImportClassification.New, ImportAction.Import),
            Entry(2, ImportClassification.Conflict, ImportAction.Skip),
            Entry(3, ImportClassification.Invalid, ImportAction.Skip));

        model.Rows[0].IsSelected = false;
        model.Rows[1].IsSelected = true;
        model.Rows[1].ConflictAction = ImportAction.KeepBoth;

        Assert.Equal(ImportAction.Skip, model.Decisions[1]);
        Assert.Equal(ImportAction.KeepBoth, model.Decisions[2]);
        Assert.Equal(ImportAction.Skip, model.Decisions[3]);
        Assert.True(model.Rows[1].IsConflictChoiceEnabled);
    }

    private static ImportReviewModel Model(params ImportPlanEntry[] entries) =>
        new(Plan(entries));

    private static ImportPlan Plan(params ImportPlanEntry[] entries) =>
        new("C:\\library", "source.zip", [1, 2, 3], entries);

    private static ImportPlanEntry Entry(int id, ImportClassification classification, ImportAction action) =>
        new(id, ImportItemKind.Response, $"source-{id}.json", $"Folder/Response {id}.json", classification, action,
            classification == ImportClassification.DuplicateElsewhere ? "Other/Copy.json" : null,
            classification is ImportClassification.Invalid or ImportClassification.Unsafe ? "Cannot import." : null, null,
            [1, 2, 3], $"C:\\library\\Folder\\Response {id}.json", null,
            classification == ImportClassification.Conflict
                ? $"C:\\library\\Folder\\Response {id} (imported).json" : null);
}
