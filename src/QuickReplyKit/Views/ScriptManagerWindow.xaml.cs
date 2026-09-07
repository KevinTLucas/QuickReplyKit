using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Scripts;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Views;

public partial class ScriptManagerWindow : Window
{
    private readonly ScriptManagerModel _model;
    private ScriptCatalogItem? _selected;
    private bool _loading;
    private bool _dirty;

    public ScriptManagerWindow(LibraryService library, Action onLibraryChanged)
    {
        InitializeComponent();
        var opened = ScriptTrustLedger.Open();
        _model = new ScriptManagerModel(library.Snapshot.LibraryRoot, opened.Value!, () =>
        {
            library.Reload();
            onLibraryChanged();
        });
        _model.Reload();
        RefreshList();
        if (!opened.Success) ShowStatus(opened.Error ?? "Script trust storage is unavailable.", true);
    }

    private void RefreshList(string? selectPath = null)
    {
        ScriptList.ItemsSource = _model.Items;
        ScriptList.SelectedItem = selectPath is null ? _model.Items.FirstOrDefault() : _model.Items.FirstOrDefault(x =>
            string.Equals(x.FullPath, selectPath, StringComparison.OrdinalIgnoreCase));
        if (ScriptList.SelectedItem is null) NewDraft();
    }

    private void ScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScriptList.SelectedItem is not ScriptCatalogItem item) return;
        _selected = item;
        _loading = true;
        NameBox.Text = item.Definition?.Name ?? item.Name;
        ExpressionBox.Text = item.Definition?.Expression ?? "";
        EnabledCheck.IsChecked = item.Definition?.Enabled == true;
        _loading = false;
        _dirty = false;
        TrustState.Text = item.Status;
        ShowStatus(item.Error ?? "Review the exact source before trusting this script.", !item.IsValid);
        UpdateButtons();
    }

    private void New_Click(object sender, RoutedEventArgs e) => NewDraft();

    private void NewDraft()
    {
        ScriptList.SelectedItem = null;
        _selected = null;
        _loading = true;
        NameBox.Clear();
        ExpressionBox.Text = "\"Hello\"";
        EnabledCheck.IsChecked = true;
        _loading = false;
        _dirty = true;
        TrustState.Text = "New · not saved or trusted";
        ShowStatus("Write an expression, then Validate or Save & trust.");
        UpdateButtons();
        NameBox.Focus();
    }

    private void Draft_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _dirty = true;
        ShowStatus("Unsaved changes");
        UpdateButtons();
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        var result = _model.Validate(NameBox.Text, ExpressionBox.Text, EnabledCheck.IsChecked == true, _selected);
        ShowStatus(result.Success ? "Expression is valid." : result.Error ?? result.Marker, !result.Success);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save(trust: false);
    private void SaveTrust_Click(object sender, RoutedEventArgs e) => Save(trust: true);

    private void Save(bool trust)
    {
        var result = _model.Save(_selected, NameBox.Text, ExpressionBox.Text,
            EnabledCheck.IsChecked == true, trust);
        if (!result.Success) { ShowStatus(result.Error ?? "The script could not be saved.", true); return; }
        _dirty = false;
        RefreshList(result.Item?.FullPath);
        ShowStatus(trust ? "Saved, reloaded, and trusted." : "Saved. This script was not newly trusted.");
    }

    private void Trust_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _dirty) return;
        var result = _model.Trust(_selected);
        if (!result.Success) { ShowStatus(result.Error ?? "The script could not be trusted.", true); return; }
        RefreshList(result.Item?.FullPath);
        ShowStatus("This exact saved source is trusted.");
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !ConfirmDialog.Confirm(this, "Delete script",
            $"Delete “{_selected.Name}”? This also removes its local trust approval.", "Delete", destructive: true)) return;
        var result = _model.Delete(_selected);
        if (!result.Success) { ShowStatus(result.Error ?? "The script could not be deleted.", true); return; }
        RefreshList();
        ShowStatus("Script deleted.");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { IsValid: true }) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export script definition",
            Filter = "QuickReplyKit script (*.qrkscript.json)|*.qrkscript.json",
            FileName = Path.GetFileName(_selected.FullPath),
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        try { File.Copy(_selected.FullPath, dialog.FileName, overwrite: true); ShowStatus("Script exported."); }
        catch (Exception ex) { ShowStatus(ex.Message, true); }
    }

    private void UpdateButtons()
    {
        bool saved = _selected is not null;
        DeleteButton.IsEnabled = saved;
        ExportButton.IsEnabled = _selected?.IsValid == true;
        TrustButton.IsEnabled = _selected is { IsValid: true, IsTrusted: false } && !_dirty;
    }

    private void ShowStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.SetResourceReference(ForegroundProperty, error ? "Error" : "TextMuted");
    }
}
