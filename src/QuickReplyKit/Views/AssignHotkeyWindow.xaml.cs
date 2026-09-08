using System.Windows;
using System.Windows.Input;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Interop;

namespace QuickReplyKit.Views;

/// <summary>
/// Captures an instant-paste hotkey for one response: a click-to-capture box (same pattern as the
/// Settings hotkey box), a rich/plain choice, and Save / Remove / Cancel. Conflict text (already used
/// by the launcher or another response) is surfaced live via the injected checker and blocks Save.
/// The caller reads <see cref="Removed"/>, <see cref="Gesture"/> and <see cref="Plain"/> on OK.
/// </summary>
public partial class AssignHotkeyWindow : Window
{
    private readonly Func<string, string?> _conflict;
    private string? _gesture;   // captured combo, null until a real one is pressed

    /// <summary>True when the user chose Remove — clear any existing bind.</summary>
    public bool Removed { get; private set; }

    /// <summary>The captured gesture (valid) when the dialog returns true and wasn't a Remove.</summary>
    public string? Gesture => _gesture;

    /// <summary>Paste plain text (vs rich) for this bind.</summary>
    public bool Plain => PastePlain.IsChecked == true;

    public AssignHotkeyWindow(string responseName, ResponseHotkey? existing, Func<string, string?> conflict)
    {
        _conflict = conflict;
        InitializeComponent();

        ResponseName.Text = responseName;

        if (existing is not null)
        {
            _gesture = existing.Gesture;
            HotkeyBox.Text = existing.Gesture;
            PastePlain.IsChecked = existing.Plain;
            PasteRich.IsChecked = !existing.Plain;
        }
        else
        {
            HotkeyBox.Text = "";
            RemoveButton.Visibility = Visibility.Collapsed;
        }
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e) => HotkeyBox.Text = "Press a combination…";

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e) => HotkeyBox.Text = _gesture ?? "";

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape || key == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            return;

        e.Handled = true;   // never let the box receive/echo a captured keystroke

        // Alt-combos surface the real key via SystemKey; a bare modifier keeps us waiting.
        string? gesture = HotkeyGesture.Format(Keyboard.Modifiers, key);
        if (gesture is null) return;

        _gesture = gesture;
        HotkeyBox.Text = gesture;

        string? conflict = _conflict(gesture);
        if (conflict is null) HideError();
        else ShowError(conflict);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        Removed = true;
        DialogResult = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!HotkeyGesture.TryParse(_gesture, out _))
        {
            ShowError("Press a shortcut: a modifier plus a key, e.g. Ctrl+Alt+1.");
            return;
        }
        if (_conflict(_gesture!) is { } conflict)
        {
            ShowError(conflict + " Pick another.");
            return;
        }
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
