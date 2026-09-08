using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
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
        Action<string, bool>? composeInsert = null, IEnumerable<string>? scriptNames = null)
    {
        InitializeComponent();
        WindowSizing.LimitHeightToCurrentScreen(this);
        _root = libraryRoot;
        _editing = editing;
        _composeInsert = composeInsert;

        if (scriptNames?.Any() == true && InsertVarButton.ContextMenu is { } variables)
        {
            variables.Items.Add(new Separator());
            foreach (string name in scriptNames)
            {
                var item = new MenuItem { Header = $"{{{{script:{name}}}}}", ToolTip = "Sandboxed script" };
                item.Click += InsertVarItem_Click;
                variables.Items.Add(item);
            }
        }

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

        CommandManager.AddExecutedHandler(Body, Body_CommandExecuted);
        Body.Document = HtmlToFlowDocument.Convert(html);
        Loaded += (_, _) =>
        {
            UpdateToolbarState();
            (composeInsert != null ? (System.Windows.UIElement)Body : NameBox).Focus();
        };
    }

    /// <summary>Open the editor to create a new response. Returns true if one was saved.</summary>
    public static bool OpenNew(Window owner, string libraryRoot, IEnumerable<string> categories,
        string defaultCategory, string html = "", IEnumerable<string>? scriptNames = null)
    {
        var dlg = new EditorWindow(libraryRoot, categories, null, defaultCategory, html,
            scriptNames: scriptNames) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    /// <summary>Open the editor to edit an existing response. Returns true if it was saved.</summary>
    public static bool OpenEdit(Window owner, string libraryRoot, IEnumerable<string> categories,
        ResponseItem item, IEnumerable<string>? scriptNames = null)
    {
        var dlg = new EditorWindow(libraryRoot, categories, item, item.CategoryPath, item.Html,
            scriptNames: scriptNames)
        { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    /// <summary>
    /// Open the editor in transient "Edit &amp; insert" mode: <paramref name="html"/> is the already
    /// resolved message. On Insert the (edited) content is handed to <paramref name="onInsert"/>
    /// (html, plain) — which runs the paste pipeline. Nothing is saved to disk.
    /// </summary>
    public static void OpenCompose(Window owner, string html, Action<string, bool> onInsert,
        IEnumerable<string>? scriptNames = null)
    {
        var dlg = new EditorWindow("", System.Array.Empty<string>(), null, "", html, onInsert, scriptNames)
        { Owner = owner };
        dlg.ShowDialog();
    }

    // --- Toolbar ------------------------------------------------------------

    private void Body_SelectionChanged(object sender, RoutedEventArgs e) => UpdateToolbarState();

    private void Body_CommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command == EditingCommands.ToggleBold || e.Command == EditingCommands.ToggleItalic ||
            e.Command == EditingCommands.ToggleUnderline || e.Command == EditingCommands.AlignLeft ||
            e.Command == EditingCommands.AlignCenter || e.Command == EditingCommands.AlignRight ||
            e.Command == EditingCommands.ToggleBullets || e.Command == EditingCommands.ToggleNumbering)
            Dispatcher.BeginInvoke((Action)UpdateToolbarState);
    }

    private void UpdateToolbarState()
    {
        SetState(BoldButton, Body.Selection.GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold);
        SetState(ItalicButton, Body.Selection.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic);

        object decorations = Body.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        SetDecorationState(UnderlineButton, decorations, TextDecorationLocation.Underline);
        SetDecorationState(StrikeButton, decorations, TextDecorationLocation.Strikethrough);

        object alignment = Body.Selection.GetPropertyValue(Paragraph.TextAlignmentProperty);
        SetState(AlignLeftButton, alignment, TextAlignment.Left);
        SetState(AlignCenterButton, alignment, TextAlignment.Center);
        SetState(AlignRightButton, alignment, TextAlignment.Right);

        (BulletListButton.IsChecked, NumberListButton.IsChecked) = ListState(Body.Selection);
    }

    private static void SetState(ToggleButton button, object value, object expected) =>
        button.IsChecked = value == DependencyProperty.UnsetValue ? null : Equals(value, expected);

    private static void SetDecorationState(ToggleButton button, object value, TextDecorationLocation location) =>
        button.IsChecked = value == DependencyProperty.UnsetValue ? null :
            value is TextDecorationCollection decorations && decorations.Any(d => d.Location == location);

    private static List? ListAt(TextPointer position)
    {
        for (DependencyObject? parent = position.Parent; parent != null; parent =
             parent is FrameworkContentElement element ? element.Parent : null)
            if (parent is List list) return list;
        return null;
    }

    internal static (bool? Bullet, bool? Number) ListState(TextRange selection)
    {
        if (selection.IsEmpty)
        {
            var list = ListAt(selection.Start.GetInsertionPosition(LogicalDirection.Forward) ?? selection.Start);
            return ListState(list);
        }

        var end = selection.End.GetInsertionPosition(LogicalDirection.Backward) ?? selection.End;
        (bool Bullet, bool Number)? state = null;
        for (var position = selection.Start; position != null && position.CompareTo(end) <= 0;
             position = position.GetNextContextPosition(LogicalDirection.Forward))
        {
            var paragraph = position.Paragraph;
            if (paragraph == null) continue;

            var current = ListState(ListAt(paragraph.ContentStart));
            if (state is not null && state.Value != current) return (null, null);
            state = current;
        }

        return state ?? (false, false);
    }

    private static (bool Bullet, bool Number) ListState(List? list) =>
        (list != null && IsBullet(list.MarkerStyle),
         list != null && list.MarkerStyle != TextMarkerStyle.None && !IsBullet(list.MarkerStyle));

    private static bool IsBullet(TextMarkerStyle marker) => marker is TextMarkerStyle.Disc or
        TextMarkerStyle.Circle or TextMarkerStyle.Square or TextMarkerStyle.Box;

    private void ClearFormatting_Click(object sender, RoutedEventArgs e)
    {
        EditorFormatting.Clear(Body.Selection);
        UpdateToolbarState();
        Body.Focus();
    }

    private void Strike_Click(object sender, RoutedEventArgs e)
    {
        EditorFormatting.ToggleStrike(Body.Selection);
        UpdateToolbarState();
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

    private void TextColor_Click(object sender, RoutedEventArgs e) => OpenMenu(TextColorButton);

    private void Highlight_Click(object sender, RoutedEventArgs e) => OpenMenu(HighlightButton);

    private static void OpenMenu(Button button)
    {
        if (button.ContextMenu is { } menu) { menu.PlacementTarget = button; menu.IsOpen = true; }
    }

    private void TextSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag })
        {
            if (tag == "auto") EditorFormatting.UseDefaultForeground(Body.Selection, Body.Foreground);
            else Body.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brush(tag));
        }
        Body.Focus();
    }

    private void HighlightSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag })
        {
            if (tag == "none") EditorFormatting.RemoveHighlight(Body.Selection);
            else Body.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brush(tag));
        }
        Body.Focus();
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
        var existingLink = EditorFormatting.SelectedLink(sel);
        string existing = existingLink?.NavigateUri?.ToString() ?? "";

        string? url = InputDialog.Prompt(this, "Insert link", "Link URL:", existing);
        if (url == null) return;
        if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri) ||
            !HtmlToFlowDocument.IsSafeLink(uri))
        {
            ShowError("Use an http://, https://, or mailto: link.");
            return;
        }

        if (existingLink != null)
        {
            existingLink.NavigateUri = uri;
            existingLink.ToolTip = uri.ToString();
            Body.Focus();
            return;
        }

        // Empty selection: drop the URL in as its own linked text.
        if (sel.IsEmpty)
        {
            sel.Text = url;
            Body.Selection.Select(sel.Start, sel.End);
        }

        if (!EditorFormatting.CanCreateLink(Body.Selection))
        {
            ShowError("Select text within one paragraph that is not already linked.");
            return;
        }

        try
        {
            var link = new Hyperlink(Body.Selection.Start, Body.Selection.End) { NavigateUri = uri };
            link.ToolTip = uri.ToString();
        }
        catch (InvalidOperationException)
        {
            ShowError("That selection can't be converted into one link.");
            return;
        }
        Body.Focus();
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
        System.Windows.Automation.AutomationProperties.SetName(remove, $"Remove tag {tag}");
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
