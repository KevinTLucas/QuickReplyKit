# Phase 4 — Rich-text Editor & Response Management

Create/edit responses visually (no markup), and full file management, all from inside the app.

## Rich-text editor (`Views/EditorWindow.xaml`)
A focused editor for support responses — NOT a word processor.
- Body: WPF `RichTextBox`. Toolbar with only: **Bold, Italic, Underline, Bullet list, Numbered
  list, Insert/Edit link, Remove formatting**. Wire to `EditingCommands.ToggleBold/ToggleItalic/
  ToggleUnderline/ToggleBullets/ToggleNumbering` (RichTextBox as command target); link = custom
  (prompt for URL, wrap selection in a `Hyperlink`). Standard Ctrl+B/I/U work automatically.
- Fields: **Name** (required; sanitized to a valid filename), **Category** (picker of existing
  folders + ability to type a NEW nested path like `Clever Apps/Canvas` that gets created on save),
  **App** (optional), **Tags** (comma-separated). Save / Cancel.
- Load existing content with `HtmlToFlowDocument`; serialize on save with the new
  **`QuickReplyKit.Core.Text.FlowDocumentToHtml.Convert(FlowDocument) -> string`** emitting ONLY the
  subset (p, br, b, i, u, ul/ol/li, a). Drop colors/fonts/sizes deliberately to keep stored HTML
  clean, portable, and diffable. Round-trip must be stable (convert→load→convert == same).

## Converters (Core, WPF types, STA-tested)
- `FlowDocumentToHtml.Convert` — walk `Paragraph`/`Run`/`Bold`/`Italic`/`Underline`/`List`/
  `ListItem`/`Hyperlink`; escape text. No other inline properties.
- Round-trip tests with `HtmlToFlowDocument` (both directions) on an STA thread.

## Response management (`QuickReplyKit.Core.ResponseManager`, headless + tested)
File-level ops (identity = filename; folders = categories), each returns the new path / result:
- **Create**(category, name, item) · **Edit/Save** (overwrite) · **Duplicate**(item) → `Name (copy).json`
  (or next free suffix) in same folder · **Rename**(item, newName) · **Move**(item, newCategory) ·
  **Delete**(item) → **Recycle Bin** via `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(...,
  RecycleOption.SendToRecycleBin)` (built-in, no NuGet; recoverable) · **CreateFolder**(path).
- Name collisions in the target folder → return a conflict result the UI turns into a prompt
  (overwrite / keep both / cancel). Sanitize names; reject empty/invalid.
- Unit tests (temp dirs): create, duplicate suffixing, rename, move across folders, delete removes
  file, createFolder makes nested dirs, collision detection.

## UI wiring (launcher)
- **New response** (+) button and **New from clipboard**: the latter reads the clipboard — if HTML
  present, sanitize to the subset; else wrap plain text in `<p>` (preserve line breaks) — and opens
  the editor pre-populated (`CreateFromClipboard`). Default category = current nav selection.
- **Results context menu** (right-click) + matching keyboard: Edit, Duplicate, Rename, Move to…,
  Delete (confirm dialog — destructive), Open containing folder (`explorer /select,<file>`), Copy.
- Tray + a launcher button: **Open Response Library** (`explorer <libraryRoot>`).
- After any change, rely on `LibraryWatcher` → `LibraryService.Updated` to refresh; keep selection
  stable where possible.

## Confirmations
- Delete shows a themed confirm dialog naming the response; only then delete (to Recycle Bin).
- Overwrite/conflict prompts as above. All destructive/irreversible actions confirm first.

## Validation (separate QA agent)
- Build clean, tests green (ResponseManager + converter round-trip). Report count.
- Screenshot the editor (empty + populated) and the results context menu; Read them to confirm layout.
- Manual-ish flow: create a response into a new nested category → appears in nav/results live;
  edit + save preserves formatting; duplicate/rename/move reflect on disk; delete asks + goes to Recycle Bin.
