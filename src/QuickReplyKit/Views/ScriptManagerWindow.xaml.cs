using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Scripts;
using QuickReplyKit.ViewModels;

namespace QuickReplyKit.Views;

public partial class ScriptManagerWindow : Window
{
    private const string ExpressionStarter = "\"Hello\"";
    private const string PowerShellStarter = "Write-Output \"Hello\"";
    private const string NativeWarningText =
        "This full-trust Windows PowerShell script runs as your signed-in Windows user. " +
        "It can read, change, or delete files; access the network, registry, environment, and credentials; " +
        "and start processes. Trust only code whose exact source you reviewed.";

    private readonly ScriptManagerModel _model;
    private ScriptCatalogItem? _selected;
    private bool _loading;
    private bool _dirty;
    private bool _allowClose;

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
        _loading = true;
        ScriptList.ItemsSource = _model.Items;
        var item = selectPath is null ? _model.Items.FirstOrDefault() : _model.Items.FirstOrDefault(x =>
            string.Equals(x.FullPath, selectPath, StringComparison.OrdinalIgnoreCase));
        ScriptList.SelectedItem = item;
        _loading = false;
        if (item is null) NewDraft();
        else Load(item);
    }

    private void ScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ScriptList.SelectedItem is not ScriptCatalogItem item) return;
        string requestedPath = item.FullPath;
        if (!TryLeaveDraft())
        {
            _loading = true;
            ScriptList.SelectedItem = _selected;
            _loading = false;
            return;
        }
        item = _model.Items.FirstOrDefault(x =>
            string.Equals(x.FullPath, requestedPath, StringComparison.OrdinalIgnoreCase)) ?? item;
        _loading = true;
        ScriptList.SelectedItem = item;
        _loading = false;
        Load(item);
    }

    private void Load(ScriptCatalogItem item)
    {
        _selected = item;
        _loading = true;
        NameBox.Text = item.Definition?.Name ?? item.Name;
        ExpressionBox.Text = item.Definition?.Expression ?? "";
        SetLanguage(item.Definition?.Language ?? ScriptDefinitionFile.Language);
        EnabledCheck.IsChecked = item.Definition?.Enabled == true;
        UpdateLanguagePresentation();
        _loading = false;
        _dirty = false;
        TrustState.Text = item.Status;
        ShowStatus(item.Error ?? "Review the exact source before trusting this script.", !item.IsValid);
        UpdateButtons();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (TryLeaveDraft()) NewDraft();
    }

    private void NewDraft()
    {
        ScriptList.SelectedItem = null;
        _selected = null;
        _loading = true;
        NameBox.Clear();
        SetLanguage(ScriptDefinitionFile.Language);
        ExpressionBox.Text = ExpressionStarter;
        EnabledCheck.IsChecked = true;
        UpdateLanguagePresentation();
        _loading = false;
        _dirty = false;
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

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ExpressionBox.Text is ExpressionStarter or PowerShellStarter)
        {
            _loading = true;
            ExpressionBox.Text = SelectedLanguage == ScriptDefinitionFile.NativeLanguage
                ? PowerShellStarter : ExpressionStarter;
            _loading = false;
        }
        UpdateLanguagePresentation();
        Draft_Changed(sender, e);
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        var result = _model.Validate(NameBox.Text, ExpressionBox.Text, EnabledCheck.IsChecked == true,
            SelectedLanguage, _selected);
        ShowStatus(result.Success ? "Source is valid." : result.Error ?? result.Marker, !result.Success);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save(trust: false);
    private void SaveTrust_Click(object sender, RoutedEventArgs e) => Save(trust: true);

    private bool Save(bool trust)
    {
        if (trust && !ConfirmTrust(SelectedLanguage, ConfirmNativeTrust)) return false;
        var result = _model.Save(_selected, NameBox.Text, ExpressionBox.Text,
            EnabledCheck.IsChecked == true, SelectedLanguage, trust);
        if (!result.Success) { ShowStatus(result.Error ?? "The script could not be saved.", true); return false; }
        _dirty = false;
        RefreshList(result.Item?.FullPath);
        ShowStatus(trust ? "Saved, reloaded, and trusted." : "Saved. This script was not newly trusted.");
        return true;
    }

    private void Trust_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Definition is not { } reviewed || _dirty) return;
        if (!ConfirmTrust(reviewed.Language, ConfirmNativeTrust)) return;
        var result = _model.Trust(_selected, reviewed);
        if (!result.Success)
        {
            if (result.Item is not null) RefreshList(result.Item.FullPath);
            ShowStatus(result.Error ?? "The script could not be trusted.", true);
            return;
        }
        RefreshList(result.Item?.FullPath);
        ShowStatus("This exact saved source is trusted.");
    }

    private bool TryLeaveDraft() => CanLeaveDraft(_dirty,
        () => ConfirmDialog.AskUnsavedDraft(this), () => Save(trust: false));

    internal static bool CanLeaveDraft(bool dirty, Func<UnsavedDraftChoice> prompt, Func<bool> save)
    {
        if (!dirty) return true;
        return prompt() switch
        {
            UnsavedDraftChoice.Save => save(),
            UnsavedDraftChoice.Discard => true,
            _ => false,
        };
    }

    internal static bool ConfirmTrust(string language, Func<bool> confirmNative) =>
        language != ScriptDefinitionFile.NativeLanguage || confirmNative();

    private bool ConfirmNativeTrust() => ConfirmDialog.Confirm(this, "Trust full-trust Windows PowerShell",
        NativeWarningText + "\n\nConfirm that you reviewed the exact source visible in the Scripts window.",
        "Trust PowerShell");

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!TryLeaveDraft()) return;
        _allowClose = true;
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (!TryLeaveDraft()) e.Cancel = true;
        else _allowClose = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close_Click(sender, e);
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

    private string SelectedLanguage => (LanguageBox.SelectedItem as ComboBoxItem)?.Tag as string
        ?? ScriptDefinitionFile.Language;

    private void SetLanguage(string language) => LanguageBox.SelectedIndex =
        language == ScriptDefinitionFile.NativeLanguage ? 1 : 0;

    private void UpdateLanguagePresentation()
    {
        bool native = SelectedLanguage == ScriptDefinitionFile.NativeLanguage;
        SourceLabel.Text = native ? "Windows PowerShell source" : "Expression source";
        AutomationProperties.SetName(ExpressionBox, native
            ? "Exact full-trust Windows PowerShell source"
            : "Exact restricted QuickReplyKit expression source");
        AutomationProperties.SetHelpText(ExpressionBox, native ? NativeWarningText
            : "Restricted QuickReplyKit expression language; no file, network, registry, environment, credential, or process access.");
        NativeWarning.Visibility = native ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.SetResourceReference(ForegroundProperty, error ? "Error" : "TextMuted");
    }
}
