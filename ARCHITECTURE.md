# QuickReplyKit — Architecture & Build Spec

A lightweight Windows response launcher for support teams. Global hotkey → find a
prewritten response → insert it into whatever app was focused, preserving the user's
clipboard and keeping our content out of Windows clipboard history.

## Stack
- **WPF, C#, .NET 10 (net10.0-windows)**, `UseWPF=true`.
- No heavyweight UI frameworks. Native WPF controls only. Win32 via P/Invoke.
- Distribution: `dotnet publish` self-contained, single-file, portable `.exe`.

## Solution layout
```
QuickReplyKit.sln
src/
  QuickReplyKit/            WPF app (main)
  QuickReplyKit.Core/       headless logic (library, search, storage, import/export, html) — no WPF refs
  QuickReplyKit.Tests/      xUnit tests over Core
tools/
  capture-window.ps1        screenshot helper for UI validation
```
Rule: **all non-UI logic lives in `QuickReplyKit.Core` and is unit-tested.** The WPF
project references Core and holds only views, view-models, interop, and app wiring.

## Storage model
- **Library root** (configurable): default `%USERPROFILE%\Documents\QuickReplyKit\Library`.
- **Category = folder.** Nesting is unlimited. Empty folders are valid categories.
- **Response = one `.json` file.** `filename (without .json) = response display name`.
  Sharing = copy folders/files into the library. Live updates via `FileSystemWatcher`.
- Response file schema (v1):
  ```json
  {
    "schema": 1,
    "app": "IXL",                 // optional: which app/product this is for
    "tags": ["licensing"],        // optional keywords
    "html": "<p>…</p>",           // rich content, restricted HTML subset (see below)
    "favorite": false,
    "created": "2026-09-05T12:00:00Z",
    "modified": "2026-09-05T12:00:00Z"
  }
  ```
  - Name is NOT stored in the file (it is the filename) so copying/renaming files "just works".
  - A `.json` that fails to parse or lacks `schema`/`html` is an **invalid response**: it is
    skipped, surfaced with an error marker, and must never block sibling valid responses.
- **App config**: `%APPDATA%\QuickReplyKit\config.json` (hotkey, window bounds+monitor,
  library path, default paste behavior). **Recent list**: `%APPDATA%\QuickReplyKit\recent.json`
  (list of relative paths + timestamps; local-only). **Favorites** live in each response file so
  they travel when shared.

## Rich text
- Supported subset: bold, italic, underline, unordered list, ordered list, hyperlink,
  paragraphs / line breaks. Nothing else (keep it a support-response editor, not Word).
- **Canonical stored form = HTML subset.** Two small converters in Core:
  - `HtmlToFlowDocument` — load stored HTML into a WPF `RichTextBox`/`FlowDocument` for edit & preview.
  - `FlowDocumentToHtml` — serialize the editor back to the HTML subset on save.
  Both are bounded (subset only) and fully unit-tested (round-trip tests).
- Plain text = HTML with tags stripped and lists/paragraphs turned into newlines.

## Paste engine (the core value)
On launcher show: capture `GetForegroundWindow()` (the app the user was in).
On insert:
1. Hide launcher.
2. `SetForegroundWindow(previous)`.
3. Snapshot current clipboard (text/html/rtf/image via WPF `Clipboard`) to restore later.
4. Put response on clipboard **with history-exclusion**: set `CF_UNICODETEXT` + `HTML Format`
   (CF_HTML), plus registered formats `ExcludeClipboardContentFromMonitorProcessing`,
   `CanIncludeInClipboardHistory`=0, `CanUploadToCloudClipboard`=0. This requires raw
   Win32 clipboard calls (`OpenClipboard`/`EmptyClipboard`/`SetClipboardData` with `GlobalAlloc`),
   not WPF `Clipboard`, because WPF can't set the exclusion formats.
5. `SendInput` Ctrl+V to the target.
6. After a short delay, **restore** the snapshotted clipboard.
- Plain-text paste = same, but only `CF_UNICODETEXT` (+ exclusion formats).
- Explicit **Copy** action = normal clipboard set (user-intended; may appear in history) —
  do NOT exclude, and do NOT restore.
- CF_HTML must be built with the correct byte-offset header (StartHTML/EndHTML/StartFragment/EndFragment).

## Windows/UI behavior
- Single instance. System tray icon with menu: Open, Open Response Library, Settings, Exit.
- Launcher window: compact, resizable, always-restores-on-screen (validate bounds against
  current monitors; if off-screen, re-center on the nearest/working monitor). Remember size+pos.
- Global hotkey configurable (default e.g. `Ctrl+Alt+Space`), via `RegisterHotKey`.
- Launcher hides (not closes) on Esc / focus loss / after insert. App keeps running in tray.
- Must NOT interfere with right-click menus in other apps (we only register a hotkey; no mouse hooks).

## Launcher UX contract
- Search box focused on open. Type → results filter live (name, app, folder, tags).
- Folder/category navigation (tree) + results list + preview pane.
- Keyboard: type → Down/Up to move selection → Enter = default paste. Single click = select+preview,
  never auto-insert. Buttons: Paste (rich), Paste as plain text, Copy.
- Favorites + Recent as quick-access views.

## Validation gates (run after each phase)
- `dotnet build` clean, `dotnet test` green.
- UI: launch with `--show` (debug flag to show launcher without hotkey) → capture PNG → inspect.
- Resize small/large, DPI scaling, long names, deep folders, empty states, invalid files, large library.
- Core E2E: paste into Notepad, assert inserted text + clipboard restored.
- Regression: full workflow — focus other app → hotkey → find → preview → insert → focus returns.
