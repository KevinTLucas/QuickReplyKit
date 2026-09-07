# QuickReplyKit — Architecture & Design

A lightweight Windows "response launcher": global hotkey → find a prewritten response → insert it at the
cursor in whatever app was focused, **preserving the user's clipboard** and **keeping the content out of
Windows clipboard history**. This doc is the current design + a map of where things live in the code.
For the running state, see [`docs/STATUS.md`](docs/STATUS.md); for how to build/run, see [`CLAUDE.md`](CLAUDE.md).

## Stack
- **WPF, C#, .NET 10** (`net10.0-windows`, `UseWPF=true`). Solution: **`QuickReplyKit.slnx`** (XML solution format).
- **No third-party packages** — BCL / native WPF only (Win32 via hand-rolled P/Invoke; zips via
  `System.IO.Compression`; JSON via `System.Text.Json`; tray via WinForms `NotifyIcon`; recycle-bin delete via
  `Microsoft.VisualBasic.FileIO`). Keeps the portable exe lean.
- Distribution: `dotnet publish` self-contained, single-file, portable `.exe`.

## Projects & where things live
```
src/
  QuickReplyKit.Core/        headless logic (net10.0-windows, no Views). Fully unit-tested.
    Model/                   ResponseItem, CategoryNode, LibrarySnapshot
    Library/                 LibraryScanner (parallel scan), LibraryWatcher (debounced), LibrarySeeder
    LibraryService.cs        coordinator: seed + scan (deferrable/off-thread) + watch; Search/FindByName/ToggleFavorite/MarkUsed
    Search/                  ResponseSearch (ranked, in-scope, lazy fuzzy, match spans), SearchHit
    Storage/                 ResponseFile, AppConfig, RecentStore, ResponseManager, FolderMeta, TrashService,
                             ThemeResolver
    WindowPlacement.cs       monitor-aware launcher bounds validation
    Diagnostics/             rolling Logger + process-wide Diag facade
    Text/                    HtmlToFlowDocument, FlowDocumentToHtml, HtmlText (plain), CfHtml, ClipboardHtml
    Templates/               TemplateEngine (built-in variables + response includes)
    ImportExport/            LibraryPorter (export/import), LibraryBackup (rolling snapshots + restore)
  QuickReplyKit/             WPF app
    Views/                   Launcher, Editor, Settings, AssignHotkey, HotkeyManager, ImportPreview, Backup, Trash,
                             About, Onboarding, Input/Confirm dialogs, HighlightText
    ViewModels/              LauncherViewModel, ResponseRowVm, NavNodeVm, RelayCommand, ViewModelBase,
                             ImportCoordinator, ImportPreview
    Interop/                 HotkeyRegistrar (many hotkeys, one msg-only window), HotkeyGesture, HotkeyConflicts (pure
                             dup/lookup logic), ForegroundWindow, ForegroundMonitor, ForegroundTargetTracker,
                             ClipboardWriter, ClipboardBackup, InputSender
    Services/                InsertService, IInsertService, HotkeyService (owns launcher + per-response binds, instant
                             paste), ThemeManager, StartupRegistration
    Themes/                  Light.xaml, Dark.xaml (DynamicResource brushes)
    App.xaml(.cs)            startup, single-instance, tray, hotkey, async initial scan; SelfTest.cs (--selftest-* gates)
  QuickReplyKit.Tests/       xUnit over Core, view-models, app services, and Win32 interop
tools/                       shoot.ps1 (launch+screenshot+kill), capture-window.ps1, make-icon.ps1
assets/DefaultLibrary/       example responses, seeded into an empty library on first run
```
Rule: **non-UI logic lives in `QuickReplyKit.Core` and is unit-tested.** The WPF project holds views,
view-models, interop, and app wiring.

## Storage model (a contract — keep back-compatible)
- **Library root** (configurable): default `%USERPROFILE%\Documents\QuickReplyKit\Library`.
- **Category = folder** (unlimited nesting; empty folders are valid). **Response = one `.json` file**;
  `filename (minus .json) = display name`. Sharing = copy folders/files in — picked up live via `FileSystemWatcher`.
- Response file (schema 1) — `ResponseFile` is **tolerant**: one wrong-typed field never invalidates a usable
  response; a file that isn't JSON or lacks `schema`/`html` is an **invalid response** (skipped/marked ⚠, never
  blocks siblings). `created`/`modified` are optional (not eagerly read from the filesystem — a perf choice):
  ```json
  { "schema": 1, "app": "IXL", "tags": ["licensing"], "html": "<p>…</p>", "favorite": false,
    "created": "…", "modified": "…" }
  ```
- A folder may contain `.qrkfolder.json`, a `FolderMeta` sidecar holding folder tags. The scanner ignores it as a
  response; direct folder copying preserves it. Export archives contain it, but current ZIP import incorrectly skips
  it; that known gap is tracked in BACKLOG. Batch apply explicitly unions those tags into descendant response files.
- **App config**: `%APPDATA%\QuickReplyKit\config.json` (launcher hotkey, library path, paste mode, window bounds,
  theme, remember-window, verbose logging, Trash retention, onboarding flag, and **per-response instant-paste
  hotkeys** keyed by library-relative path). **Recent** is `%APPDATA%\QuickReplyKit\recent.json`; Favorites remain
  in response files so they travel when shared.
- Other local state: rolling logs in `%APPDATA%\QuickReplyKit\logs`, rolling library backups in `backups`, and
  recoverable deleted responses in `trash`. None is scanned as library content.

## Rich text & templates
- **Canonical stored form = a restricted HTML subset**: bold, italic, underline, **strikethrough**, ul/ol/li,
  hyperlink, paragraphs — plus **font size/heading, text color, highlight, paragraph alignment** (added
  additively via inline styles/simple tags; **older files still load**). Converters in `Core/Text`:
  `HtmlToFlowDocument` (render/edit), `FlowDocumentToHtml` (serialize), `HtmlText.ToPlainText` (plain), and
  `CfHtml` (CF_HTML clipboard payload with a byte-exact StartHTML/EndHTML/StartFragment/EndFragment header).
- **New from Clipboard** routes CF_HTML through `ClipboardHtml.Sanitize`: active, hidden, and non-content blocks are
  removed, then the existing parser/serializer normalizes the remainder to the stored subset. Hyperlinks are limited
  to `http`, `https`, and `mailto`; plain clipboard text is HTML-encoded before editing.
- **Templates** (`Core/Templates/TemplateEngine`): content may contain `{{…}}` tokens resolved at insert/preview/copy:
  built-in variables (`{{greeting}}`, `{{date}}`, `{{time}}`, `{{day}}`, `{{date:<fmt>}}`) and **includes**
  (`{{Response Name}}` splices another response's resolved HTML). Cycle detection, a fan-out expansion budget, and
  inline `[missing:…]`/`[cycle:…]`/`[includes: too many]` markers. **Built-ins are a fixed allow-list — no code is
  ever executed** (responses are shareable, so this is a hard security boundary).

## Paste engine (the core value) — `Interop/*` + `Services/InsertService`
- A `ForegroundMonitor` (WinEvent `EVENT_SYSTEM_FOREGROUND`, `WINEVENT_SKIPOWNPROCESS`) continuously records the
  last real (non-QuickReplyKit) foreground window into a shared `ForegroundTargetTracker` — so paste targets the
  app the user was in no matter how the launcher is summoned (a show-time capture is a fallback).
- On insert: snapshot the clipboard (`ClipboardBackup`) → restore foreground to the target (`ForegroundWindow`,
  with the AttachThreadInput focus-restore trick) → write the response with **history exclusion** via raw Win32
  (`ClipboardWriter`: `CF_UNICODETEXT` + `HTML Format`, plus `ExcludeClipboardContentFromMonitorProcessing`,
  `CanIncludeInClipboardHistory=0`, `CanUploadToCloudClipboard=0`) → `SendInput` Ctrl+V (`InputSender`) → after a
  short delay, restore the snapshot. Plain paste = text only (+ exclusion + restore). **Copy** = a normal clipboard
  set (user-intended; no exclusion/restore). Templates are resolved before building the payload.
- No mouse/keyboard hooks — only `RegisterHotKey` — so other apps' right-click menus are never affected.

## Windows/UI behaviour
- Single instance; system tray (Open / Open Library / Import / Export / Settings / Exit). Global hotkey via
  `RegisterHotKey` (default **Ctrl+Alt+D**, configurable). Launcher **hides** (not closes) on Esc/focus-loss/insert.
- Compact, resizable, keyboard-first launcher (3 panes: nav / results / preview; nav auto-collapses when narrow).
  Window size/position + monitor remembered and always re-opened on-screen (`WindowPlacement.EnsureVisible`).
- **Startup is non-blocking:** the window shows immediately in a "Loading library…" state; the initial scan runs
  on a background task (parallelized) and populates via the watcher's `Updated`→Dispatcher path.
- **Theming:** `Themes/Light.xaml`/`Dark.xaml` swapped at runtime by `ThemeManager`; all views use `DynamicResource`.
  `AppConfig.Theme` = System/Light/Dark (System follows Windows). Per-Monitor-v2 DPI via `app.manifest`.
- The editor uses a themed ComboBox template, standard text/highlight palettes, removable tag chips, and a visible
  launcher Edit action. Category context actions edit folder tags and recursively batch-apply them.
- Import is two-stage: `ImportPreview.Analyze` performs a destination/content comparison, then `LibraryPorter.Import`
  applies one Skip/Overwrite/Keep-both policy after confirmation. A rolling backup is created before the write.
  Current preview states are New, Changed, and same-path Identical. Preview/import policy alignment, preview path
  containment, and folder-sidecar import are known gaps tracked in BACKLOG.
- `LibraryBackup` writes a versioned ZIP containing `library/`, `config.json` when present, and a manifest. Retention
  keeps at most 10 snapshots within count, 500 MB, and 90-day bounds while preserving at least the newest. Restore
  first protects the selected archive, validates and size-bounds extraction beside the live library, snapshots the current state,
  then swaps directories. A failed install rolls the exact original directory back; older library-only ZIPs remain
  restorable without changing settings. Restored config is retargeted to the actual restored library folder; the old
  launcher's final config save is suppressed and QuickReplyKit restarts so restored settings become authoritative.
  `TrashService` soft-deletes individual responses, refuses
  overwrite/path escape on restore, and purges expired entries at startup.
- `Logger` uses one background writer, level gating, and size rollover (about 10 MB x 5 files). AppDomain,
  Dispatcher, and unobserved-task exceptions are recorded but not silently swallowed. Logs omit response bodies.
- First launch schedules a three-card onboarding dialog after persisting `OnboardingSeen`. About/Diagnostics is a
  copyable, read-only environment snapshot. Settings links to hotkeys, backups, Trash, logs, and diagnostics.

## Known completion boundaries

The implemented paths above are merged and tested. The remaining contract gaps are intentionally explicit in
[`docs/BACKLOG.md`](docs/BACKLOG.md): import/backup correctness; full accessibility; backup coverage/retention;
folder Trash and retention UI; complete logging and folder-tag scope controls; richer import review plus any future
code-import gate; and refreshing About for actual launcher/response-hotkey state and backup/Trash paths.

## Validation
- `dotnet build` clean + `dotnet test` green; the paste engine has E2E self-tests (`--selftest-paste`, `-rich`,
  `-ui`, `-foreground`). UI is validated with `tools/shoot.ps1` screenshots (read the PNG). Two human spot-checks
  remain: a live hotkey paste into a third-party app, and confirming pasted text is absent from Win+V history.
