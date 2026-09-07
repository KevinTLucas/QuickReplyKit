using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using QuickReplyKit.Core;
using QuickReplyKit.Core.Diagnostics;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Interop;
using QuickReplyKit.Services;

namespace QuickReplyKit.Views;

/// <summary>
/// Modal preferences dialog. Edits a working copy of the fields and only writes back to
/// <see cref="AppConfig"/> on Save; the hotkey is rebound (with an inline "in use" message on
/// failure) and a changed library path re-points the live <see cref="LibraryService"/>.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly HotkeyService _hotkeys;
    private readonly LibraryService _library;
    private readonly Action _onLibraryChanged;

    private string _gesture;      // captured hotkey (edited in place by the capture box)
    private string _libraryPath;  // chosen library folder (may differ from _config until Save)
    private bool _saved;          // set on Save so Close doesn't revert the (persisted) theme
    private bool _loaded;         // gates live theme preview until the radios are initialised

    public SettingsWindow(AppConfig config, HotkeyService hotkeys, LibraryService library, Action onLibraryChanged)
    {
        _config = config;
        _hotkeys = hotkeys;
        _library = library;
        _onLibraryChanged = onLibraryChanged;
        _gesture = config.HotkeyGesture;
        _libraryPath = config.LibraryPath;

        InitializeComponent();

        HotkeyBox.Text = _gesture;
        LibraryPathBox.Text = _libraryPath;
        PasteRich.IsChecked = config.DefaultPasteMode == PasteMode.RichText;
        PastePlain.IsChecked = config.DefaultPasteMode == PasteMode.PlainText;
        ThemeSystem.IsChecked = config.Theme == AppTheme.System;
        ThemeLight.IsChecked = config.Theme == AppTheme.Light;
        ThemeDark.IsChecked = config.Theme == AppTheme.Dark;
        RememberWindowCheck.IsChecked = config.RememberWindow;
        StartupCheck.IsChecked = StartupRegistration.IsEnabled();
        VerboseLogCheck.IsChecked = config.VerboseLogging;
        _loaded = true;

        // Revert a live-previewed theme if the dialog is dismissed without saving.
        Closed += (_, _) => { if (!_saved) ThemeManager.Apply(_config.Theme); };
    }

    private AppTheme SelectedTheme() =>
        ThemeLight.IsChecked == true ? AppTheme.Light :
        ThemeDark.IsChecked == true ? AppTheme.Dark : AppTheme.System;

    // Live preview: apply the picked theme immediately (persisted only on Save).
    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (_loaded) ThemeManager.Apply(SelectedTheme());
    }

    // --- Hotkey capture -----------------------------------------------------

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e) => HotkeyBox.Text = "Press a combination…";

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e) => HotkeyBox.Text = _gesture;

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;   // never let the box receive/echo the keystroke

        // Alt-combos surface the real key via SystemKey; a bare modifier keeps us waiting.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        string? gesture = HotkeyGesture.Format(Keyboard.Modifiers, key);
        if (gesture is null) return;

        _gesture = gesture;
        HotkeyBox.Text = gesture;
        HideHotkeyError();
    }

    // --- Library path -------------------------------------------------------

    private void ChangeLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose the response library folder",
            InitialDirectory = Directory.Exists(_libraryPath) ? _libraryPath : "",
        };
        if (dlg.ShowDialog(this) == true)
        {
            _libraryPath = dlg.FolderName;
            LibraryPathBox.Text = _libraryPath;
        }
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_libraryPath);
            Process.Start(new ProcessStartInfo(_libraryPath) { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => App.OpenLogsFolder();

    private void ManageHotkeys_Click(object sender, RoutedEventArgs e) =>
        (Application.Current as App)?.OpenHotkeyManager();

    // --- Save ---------------------------------------------------------------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!HotkeyGesture.TryParse(_gesture, out _))
        {
            ShowHotkeyError("That isn't a valid shortcut. Use a modifier plus a key, e.g. Ctrl+Alt+D.");
            return;
        }

        // Rebind before persisting so a rejected shortcut never gets saved (or lost). TryRebindLauncher
        // rolls back to the working combo itself on failure (and re-registers all binds either way).
        if (_gesture != _config.HotkeyGesture && !_hotkeys.TryRebindLauncher(_gesture))
        {
            ShowHotkeyError("That shortcut is in use — pick another.");
            return;
        }

        _config.HotkeyGesture = _gesture;
        _config.DefaultPasteMode = PastePlain.IsChecked == true ? PasteMode.PlainText : PasteMode.RichText;
        _config.Theme = SelectedTheme();
        _config.RememberWindow = RememberWindowCheck.IsChecked == true;
        _config.VerboseLogging = VerboseLogCheck.IsChecked == true;
        Diag.SetVerbose(_config.VerboseLogging);   // takes effect immediately, no restart

        bool libraryChanged = !string.Equals(_libraryPath, _config.LibraryPath, StringComparison.OrdinalIgnoreCase);
        if (libraryChanged) _config.LibraryPath = _libraryPath;

        _config.Save();
        StartupRegistration.SetEnabled(StartupCheck.IsChecked == true);

        if (libraryChanged)
        {
            _library.Repoint(_libraryPath);
            _onLibraryChanged();
        }

        _saved = true;
        DialogResult = true;
    }

    private void ShowHotkeyError(string message)
    {
        HotkeyError.Text = message;
        HotkeyError.Visibility = Visibility.Visible;
    }

    private void HideHotkeyError() => HotkeyError.Visibility = Visibility.Collapsed;
}
