using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualBasic.FileIO;
using QuickReplyKit.Core.Model;
using QuickReplyKit.Core.Storage;
using QuickReplyKit.Core.Text;

namespace QuickReplyKit.Views;

/// <summary>
/// Rich-text editor for a single response. Content is a WPF <see cref="RichTextBox"/> whose
/// document round-trips through the Core subset converters; the fields map to
/// <see cref="ResponseItem"/>. Persistence goes through <see cref="ResponseManager"/> /
/// <see cref="ResponseFile"/>, with name-collision prompts resolved here. On success the on-disk
/// change is picked up by the launcher's file watcher, so this window just closes.
/// </summary>
public partial class EditorWindow : Window
{
    private readonly string _root;
    private readonly ResponseItem? _editing;   // null = creating a new response
    private readonly Action<string, bool>? _composeInsert;   // non-null = "Edit & insert" one-off mode

    private EditorWindow(string libraryRoot, IEnumerable<string> categories,
        ResponseItem? editing, string category, string html,
        Action<string, bool>? composeInsert = null)
    {
        InitializeComponent();
        _root = libraryRoot;
        _editing = editing;
        _composeInsert = composeInsert;

        foreach (var c in categories) CategoryBox.Items.Add(c);
        CategoryBox.Text = category;

        if (composeInsert != null)
        {
            // Transient compose-and-insert: no file fields, Insert instead of Save.
            Title = "Edit & insert";
            MetaRow1.Visibility = Visibility.Collapsed;
            MetaRow2.Visibility = Visibility.Collapsed;
            SaveButton.Visibility = Visibility.Collapsed;
            SaveButton.IsDefault = false;
            InsertButton.Visibility = Visibility.Visible;
            InsertButton.IsDefault = true;
            InsertPlainButton.Visibility = Visibility.Visible;
        }
        else if (editing != null)
        {
            Title = "Edit response";
            NameBox.Text = editing.Name;
            AppBox.Text = editing.App;
            foreach (var t in editing.Tags) AddTag(t);
        }
        else
        {
            Title = "New response";
        }

        Body.Document = HtmlToFlowDocument.Convert(html);
        Loaded += (_, _) => (composeInsert != null ? (System.Windows.UIElement)Body : NameBox).Focus();
    }

    /// <summary>Open the editor to create a new response. Returns true if one was saved.</summary>
    public static bool OpenNew(Window owner, string libraryRoot, IEnumerable<string> categories,
        string defaultCategory, string html = "")
    {
        var dlg = new EditorWindow(libraryRoot, categories, null, defaultCategory, html) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    /// <summary>Open the editor to edit an existing response. Returns true if it was saved.</summary>
    public static bool OpenEdit(Window owner, string libraryRoot, IEnumerable<string> categories,
        ResponseItem item)
    {
        var dlg = new EditorWindow(libraryRoot, categories, item, item.CategoryPath, item.Html)
        { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    /// <summary>
    /// Open the editor in transient "Edit &amp; insert" mode: <paramref name="html"/> is the already
    /// resolved message. On Insert the (edited) content is handed to <paramref name="onInsert"/>
    /// (html, plain) — which runs the paste pipeline. Nothing is saved to disk.
    /// </summary>
    public static void OpenCompose(Window owner, string html, Action<string, bool> onInsert)
    {
        var dlg = new EditorWindow("", System.Array.Empty<string>(), null, "", html, onInsert)
        { Owner = owner };
        dlg.ShowDialog();
    }

    // --- Toolbar ------------------------------------------------------------

    private void ClearFormatting_Click(object sender, RoutedEventArgs e)
    {
        Body.Selection.ClearAllProperties();   // clears colour/highlight/size/decorations too
        Body.Focus();
    }

    private void Strike_Click(object sender, RoutedEventArgs e)
    {
        var cur = Body.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        bool on = cur != null && cur.Any(d => d.Location == TextDecorationLocation.Strikethrough);
        Body.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
            on ? new TextDecorationCollection() : TextDecorations.Strikethrough);
        Body.Focus();
    }

    private void Size_Click(object sender, RoutedEventArgs e)
    {
        if (SizeButton.ContextMenu is { } m) { m.PlacementTarget = SizeButton; m.IsOpen = true; }
    }

    private void SizeItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && double.TryParse(tag, out double size))
        {
            Body.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            Body.Focus();
        }
    }

    private void TextColor_Click(object sender, RoutedEventArgs e) => TextColorPopup.IsOpen = true;

    private void Highlight_Click(object sender, RoutedEventArgs e) => HighlightPopup.IsOpen = true;

    private void TextSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            if (tag == "auto") ClearForeground(Body.Selection);   // → inherits theme colour
            else Body.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brush(tag));
        }
        TextColorPopup.IsOpen = false;
        Body.Focus();
    }

    private void HighlightSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
            Body.Selection.ApplyPropertyValue(TextElement.BackgroundProperty,
                tag == "none" ? System.Windows.Media.Brushes.Transparent : Brush(tag));
        HighlightPopup.IsOpen = false;
        Body.Focus();
    }

    // Clear an explicit text colour over the selection so it inherits the theme (WPF has no direct
    // "clear property over range": re-apply the current colour to split runs at the selection edges,
    // then ClearValue the now-fully-selected runs). UnsetValue via ApplyPropertyValue throws.
    private static void ClearForeground(TextSelection sel)
    {
        if (sel.GetPropertyValue(TextElement.ForegroundProperty) is System.Windows.Media.Brush b)
            sel.ApplyPropertyValue(TextElement.ForegroundProperty, b);
        for (var p = sel.Start; p != null && p.CompareTo(sel.End) < 0;
             p = p.GetNextContextPosition(LogicalDirection.Forward))
            (p.Parent as Run)?.ClearValue(TextElement.ForegroundProperty);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void InsertVar_Click(object sender, RoutedEventArgs e)
    {
        if (InsertVarButton.ContextMenu is { } m) { m.PlacementTarget = InsertVarButton; m.IsOpen = true; }
    }

    private void InsertVarItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Header is not string token) return;
        Body.Selection.Text = token;                 // replace any selection, else insert at the caret
        Body.CaretPosition = Body.Selection.End;     // leave the caret just after the inserted token
        Body.Focus();
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        var sel = Body.Selection;
        string existing = FindHyperlink(sel.Start)?.NavigateUri?.ToString() ?? "";

        string? url = InputDialog.Prompt(this, "Insert link", "Link URL:", existing);
        if (url == null) return;
        if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri))
        {
            ShowError("That doesn't look like a valid URL (include http:// or https://).");
            return;
        }

        // Empty selection: drop the URL in as its own linked text.
        if (sel.IsEmpty)
        {
            sel.Text = url;
            Body.Selection.Select(sel.Start, sel.End);
        }

        if (!Body.Selection.IsEmpty)
        {
            var link = new Hyperlink(Body.Selection.Start, Body.Selection.End) { NavigateUri = uri };
            link.ToolTip = uri.ToString();
        }
        Body.Focus();
    }

    /// <summary>Walk up from a text position to the enclosing <see cref="Hyperlink"/>, if any.</summary>
    private static Hyperlink? FindHyperlink(TextPointer pos)
    {
        for (DependencyObject? p = pos.Parent; p != null; )
        {
            if (p is Hyperlink h) return h;
            p = p is FrameworkContentElement fce ? fce.Parent : null;
        }
        return null;
    }

    // --- Save ---------------------------------------------------------------

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // --- Edit & insert (transient compose) ----------------------------------

    private void Insert_Click(object sender, RoutedEventArgs e) => ComposeInsert(plain: false);
    private void InsertPlain_Click(object sender, RoutedEventArgs e) => ComposeInsert(plain: true);

    private void ComposeInsert(bool plain)
    {
        string html = FlowDocumentToHtml.Convert(Body.Document);
        _composeInsert?.Invoke(html, plain);   // resolve + paste into the target, hide the launcher
        DialogResult = true;                   // close the editor (saves nothing)
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();

        string? name = ResponseManager.Sanitize(NameBox.Text);
        if (name == null) { ShowError("Please enter a name (letters, numbers, spaces)."); return; }

        string category = (CategoryBox.Text ?? "").Trim();
        string html = FlowDocumentToHtml.Convert(Body.Document);
        CommitTagInput();                 // fold any half-typed tag into a chip first
        var tags = CurrentTags();
        string app = (AppBox.Text ?? "").Trim();

        if (_editing == null)
            SaveNew(name, category, app, tags, html);
        else
            SaveEdit(name, category, app, tags, html);
    }

    private void SaveNew(string name, string category, string app, IReadOnlyList<string> tags, string html)
    {
        var item = new ResponseItem { App = app, Tags = tags, Html = html };
        string tryName = name;
        while (true)
        {
            var res = ResponseManager.Create(_root, category, tryName, item);
            switch (res.Kind)
            {
                case ResultKind.Ok:
                    DialogResult = true;
                    return;
                case ResultKind.InvalidName:
                    ShowError(res.Error ?? "That name can't be used.");
                    return;
                case ResultKind.Conflict:
                    switch (ConfirmDialog.AskConflict(this, tryName))
                    {
                        case ConflictChoice.Overwrite:
                            Recycle(res.Existing);
                            break;  // retry Create; the path is now free
                        case ConflictChoice.KeepBoth:
                            tryName = Bump(tryName);
                            break;
                        default:
                            return; // cancel
                    }
                    break;
            }
        }
    }

    private void SaveEdit(string name, string category, string app, IReadOnlyList<string> tags, string html)
    {
        // Persist the content in place first, then relocate/rename if those fields changed.
        var item = _editing!;
        item.App = app;
        item.Tags = tags;
        item.Html = html;
        ResponseFile.Save(item, _root);

        if (!Relocate(ref item, m => ResponseManager.Move(item, category))) return;
        if (!Relocate(ref item, m => ResponseManager.Rename(item, name))) return;

        DialogResult = true;
    }

    /// <summary>Run one relocate op (Move/Rename), resolving a conflict, and refresh <paramref name="item"/>
    /// from the resulting path. Returns false only when the user cancels.</summary>
    private bool Relocate(ref ResponseItem item, System.Func<ResponseItem, ManageResult> op)
    {
        while (true)
        {
            var res = op(item);
            switch (res.Kind)
            {
                case ResultKind.Ok:
                    item = ResponseFile.Load(res.Path, _root);
                    return true;
                case ResultKind.InvalidName:
                    ShowError(res.Error ?? "That name can't be used.");
                    return false;
                case ResultKind.Conflict:
                    var choice = ConfirmDialog.AskConflict(this, System.IO.Path.GetFileNameWithoutExtension(res.Existing ?? ""));
                    if (choice == ConflictChoice.Overwrite) { Recycle(res.Existing); break; }
                    return false;   // keep-both isn't meaningful for a move/rename; treat as cancel
            }
        }
    }

    // --- Helpers ------------------------------------------------------------

    private static void Recycle(string? path)
    {
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    private static string Bump(string name)
    {
        // "Foo" -> "Foo (2)", "Foo (2)" -> "Foo (3)".
        var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.*?) \((\d+)\)$");
        return m.Success
            ? $"{m.Groups[1].Value} ({int.Parse(m.Groups[2].Value) + 1})"
            : $"{name} (2)";
    }

    // --- Tag chips ----------------------------------------------------------
    // Chips live in TagsPanel before the TagInput textbox; each Border.Tag holds its label.

    // Muted chip palette (light/dark-agnostic — pastel bg reads on both themes with dark text).
    private static readonly string[] ChipColors =
        { "#DBEAFE", "#DCFCE7", "#FEF9C3", "#FCE7F3", "#FFEDD5", "#EDE9FE", "#CFFAFE", "#F3E8FF" };

    private void TagInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.OemComma or Key.Tab)
        {
            if (CommitTagInput()) e.Handled = true;   // Tab still moves focus if nothing to commit
        }
        else if (e.Key == Key.Back && TagInput.Text.Length == 0)
        {
            RemoveLastChip();
            e.Handled = true;
        }
    }

    private void TagInput_LostFocus(object sender, RoutedEventArgs e) => CommitTagInput();

    private bool CommitTagInput()
    {
        string text = TagInput.Text.Trim().TrimEnd(',');
        if (text.Length == 0) return false;
        TagInput.Clear();
        AddTag(text);
        return true;
    }

    private void AddTag(string tag)
    {
        tag = tag.Trim();
        if (tag.Length == 0 || CurrentTags().Contains(tag, StringComparer.OrdinalIgnoreCase)) return;

        var bg = Brush(ChipColors[StableHash(tag) % ChipColors.Length]);
        var label = new TextBlock { Text = tag, Foreground = System.Windows.Media.Brushes.Black,
            VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        var remove = new Button { Content = "✕", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(2, 0, 2, 0),
            Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = System.Windows.Media.Brushes.Black, Cursor = System.Windows.Input.Cursors.Hand, FontSize = 10 };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(label);
        panel.Children.Add(remove);
        var chip = new Border { Background = bg, CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 2, 5, 2), Margin = new Thickness(2), Child = panel, Tag = tag };
        remove.Click += (_, _) => TagsPanel.Children.Remove(chip);

        TagsPanel.Children.Insert(TagsPanel.Children.IndexOf(TagInput), chip);
    }

    private void RemoveLastChip()
    {
        for (int i = TagsPanel.Children.IndexOf(TagInput) - 1; i >= 0; i--)
            if (TagsPanel.Children[i] is Border) { TagsPanel.Children.RemoveAt(i); return; }
    }

    private IReadOnlyList<string> CurrentTags() =>
        TagsPanel.Children.OfType<Border>().Select(b => (string)b.Tag).ToList();

    // Deterministic (process-stable) hash so a given tag always gets the same chip colour.
    private static int StableHash(string s)
    {
        unchecked { int h = 17; foreach (char c in s.ToLowerInvariant()) h = h * 31 + c; return h & 0x7fffffff; }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
