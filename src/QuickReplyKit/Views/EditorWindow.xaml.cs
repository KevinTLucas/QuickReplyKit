using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

    private EditorWindow(string libraryRoot, IEnumerable<string> categories,
        ResponseItem? editing, string category, string html)
    {
        InitializeComponent();
        _root = libraryRoot;
        _editing = editing;

        foreach (var c in categories) CategoryBox.Items.Add(c);
        CategoryBox.Text = category;

        if (editing != null)
        {
            Title = "Edit response";
            NameBox.Text = editing.Name;
            AppBox.Text = editing.App;
            TagsBox.Text = string.Join(", ", editing.Tags);
        }
        else
        {
            Title = "New response";
        }

        Body.Document = HtmlToFlowDocument.Convert(html);
        Loaded += (_, _) => NameBox.Focus();
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

    // --- Toolbar ------------------------------------------------------------

    private void ClearFormatting_Click(object sender, RoutedEventArgs e)
    {
        Body.Selection.ClearAllProperties();
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();

        string? name = ResponseManager.Sanitize(NameBox.Text);
        if (name == null) { ShowError("Please enter a name (letters, numbers, spaces)."); return; }

        string category = (CategoryBox.Text ?? "").Trim();
        string html = FlowDocumentToHtml.Convert(Body.Document);
        var tags = ParseTags(TagsBox.Text);
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

    private static IReadOnlyList<string> ParseTags(string? raw) =>
        (raw ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
