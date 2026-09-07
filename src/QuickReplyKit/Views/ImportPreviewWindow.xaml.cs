using System.Windows;
using System.Windows.Media;
using QuickReplyKit.Core.ImportExport;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Views;

/// <summary>
/// Shows the pre-import diff (New / Identical / Conflict) and, when there are conflicts, the policy to
/// apply to them. Returns the chosen <see cref="ConflictPolicy"/> via <see cref="Prompt"/>, or null on
/// cancel. Identical entries are shown for transparency; the import treats them as no-ops.
/// </summary>
public partial class ImportPreviewWindow : Window
{
    private sealed record Row(string Kind, string Dest, System.Windows.Media.Brush Badge);

    public ConflictPolicy Policy { get; private set; } = ConflictPolicy.Skip;

    private ImportPreviewWindow(IReadOnlyList<ImportEntry> entries)
    {
        InitializeComponent();

        int New = ImportPreview.Count(entries, ImportKind.New);
        int changed = ImportPreview.Count(entries, ImportKind.Conflict);
        int identical = ImportPreview.Count(entries, ImportKind.Identical);
        Summary.Text = $"{New} new · {changed} changed · {identical} identical (skipped)";

        EntryList.ItemsSource = entries.Select(e => new Row(
            e.Kind switch { ImportKind.New => "NEW", ImportKind.Conflict => "CHANGED", _ => "IDENTICAL" },
            e.Dest,
            Hex(e.Kind switch { ImportKind.New => "#BBF7D0", ImportKind.Conflict => "#FDE68A", _ => "#E5E7EB" })))
            .ToList();

        ConflictPanel.Visibility = changed > 0 ? Visibility.Visible : Visibility.Collapsed;
        ImportButton.IsEnabled = New > 0 || changed > 0;   // nothing to do if all identical
    }

    /// <summary>Show the preview; returns the policy to import with, or null if cancelled.</summary>
    public static ConflictPolicy? Prompt(Window owner, IReadOnlyList<ImportEntry> entries)
    {
        var dlg = new ImportPreviewWindow(entries) { Owner = owner };
        if (dlg.ShowDialog() != true) return null;
        return dlg.Policy;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        Policy = PolicyOverwrite.IsChecked == true ? ConflictPolicy.Overwrite
               : PolicyKeepBoth.IsChecked == true ? ConflictPolicy.KeepBoth
               : ConflictPolicy.Skip;
        DialogResult = true;
    }

    private static SolidColorBrush Hex(string hex)
    {
        var b = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }
}
