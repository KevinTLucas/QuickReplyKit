using System.Windows;
using System.Windows.Controls;

namespace QuickReplyKit.Views;

/// <summary>Outcome of a name collision the user must resolve.</summary>
public enum ConflictChoice { Overwrite, KeepBoth, Cancel }

/// <summary>
/// Minimal themed message dialog with configurable buttons. Used for the destructive delete
/// confirm (Delete / Cancel) and the overwrite/keep-both/cancel conflict prompt. Returns the
/// index of the chosen button, or -1 when dismissed.
/// </summary>
public partial class ConfirmDialog : Window
{
    private int _result = -1;

    private ConfirmDialog(string title, string message, (string Label, bool Danger, bool Default)[] buttons)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;

        for (int i = 0; i < buttons.Length; i++)
        {
            var (label, danger, isDefault) = buttons[i];
            int index = i;
            var b = new Button
            {
                Content = label,
                Style = (Style)Resources[danger ? "DangerButton" : "ActionButton"],
                IsDefault = isDefault,
                IsCancel = label.Equals("Cancel", System.StringComparison.OrdinalIgnoreCase),
            };
            b.Click += (_, _) => { _result = index; DialogResult = true; };
            ButtonBar.Children.Add(b);
        }
    }

    private static int Show(Window owner, string title, string message,
        params (string Label, bool Danger, bool Default)[] buttons)
    {
        var dlg = new ConfirmDialog(title, message, buttons) { Owner = owner };
        dlg.ShowDialog();
        return dlg._result;
    }

    /// <summary>Yes/No style confirm; returns true only when the affirmative button is chosen.</summary>
    public static bool Confirm(Window owner, string title, string message,
        string confirmLabel, bool destructive = false) =>
        Show(owner, title, message,
            (confirmLabel, destructive, !destructive),
            ("Cancel", false, false)) == 0;

    /// <summary>Single-button informational dialog (e.g. an import summary).</summary>
    public static void Info(Window owner, string title, string message) =>
        Show(owner, title, message, ("OK", false, true));

    /// <summary>Three-way name-collision prompt.</summary>
    public static ConflictChoice AskConflict(Window owner, string name) =>
        Show(owner, "Name already exists",
            $"A response named “{name}” already exists here.\nWhat would you like to do?",
            ("Overwrite", true, false),
            ("Keep both", false, true),
            ("Cancel", false, false)) switch
        {
            0 => ConflictChoice.Overwrite,
            1 => ConflictChoice.KeepBoth,
            _ => ConflictChoice.Cancel,
        };
}
