using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace QuickReplyKit.Views;

/// <summary>
/// Minimal themed text-entry dialog (rename / new folder / move target). The editable ComboBox
/// doubles as a plain text box when no suggestions are supplied, and as a pick-or-type control
/// when they are. Returns the entered text via <see cref="Prompt"/>, or null on cancel.
/// </summary>
public partial class InputDialog : Window
{
    private InputDialog(string title, string prompt, string initial, IEnumerable<string>? suggestions)
    {
        InitializeComponent();
        Title = title;
        PromptLabel.Text = prompt;
        if (suggestions != null)
            foreach (var s in suggestions) Input.Items.Add(s);
        Input.Text = initial;
    }

    /// <summary>Show the dialog modally; returns the trimmed text, or null if cancelled/empty.</summary>
    public static string? Prompt(Window owner, string title, string prompt,
        string initial = "", IEnumerable<string>? suggestions = null)
    {
        var dlg = new InputDialog(title, prompt, initial, suggestions) { Owner = owner };
        if (dlg.ShowDialog() != true) return null;
        var text = dlg.Input.Text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>Show an inline error and keep the dialog open (used for a rejected name).</summary>
    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void Input_Loaded(object sender, RoutedEventArgs e)
    {
        // Focus the editable text portion and select all so typing replaces the initial value.
        if (Input.Template.FindName("PART_EditableTextBox", Input) is TextBox tb)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Input.Text)) { ShowError("Please enter a value."); return; }
        DialogResult = true;
    }
}
