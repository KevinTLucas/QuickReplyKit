# QuickReplyKit — Architecture & Design

A lightweight Windows "response launcher": global hotkey → find a prewritten response → insert it at the
cursor in whatever app was focused, **preserving the user's clipboard** and marking temporary content so Windows
**should not retain it in clipboard history**. This doc is the current design + a map of where things live in the code.
For the running state, see [`docs/STATUS.md`](docs/STATUS.md); for build, test, packaging, and dependencies, see
[`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md). The root [`README.md`](README.md) is the end-user guide.

## Stack
- **WPF, C#, .NET 10** (`net10.0-windows`, `UseWPF=true`). Solution: **`QuickReplyKit.slnx`** (XML solution format).
- **No third-party product/runtime packages** — BCL / native WPF only (Win32 via hand-rolled P/Invoke; zips via
  `System.IO.Compression`; JSON via `System.Text.Json`; tray via WinForms `NotifyIcon`; recycle-bin delete via
  `Microsoft.VisualBasic.FileIO`). The test project uses xUnit/Test SDK packages only; none ships. This keeps the
  portable exe lean.
- Distribution: `dotnet publish` self-contained, single-file, portable `.exe`.

## Projects & where things live
```
src/
  QuickReplyKit.Core/        headless logic (net10.0-windows, no Views). Fully unit-tested.
    Model/                   ResponseItem, CategoryNode, LibrarySnapshot
    Library/                 LibraryScanner (parallel scan), LibraryWatcher (debounced), LibrarySeeder
    LibraryService.cs        coordinator: seed + scan (deferrable/off-thread) + watch; Search/FindByName/ToggleFavorite/MarkUsed
    Search/                  ResponseSearch (ranked, in-scope, lazy fuzzy, match spans), SearchHit
    Storage/                 ResponseFile, AppConfig, RecentStore, ResponseManager, TrashService,
                             ThemeResolver
    WindowPlacement.cs       monitor-aware launcher bounds validation
    Diagnostics/             rolling Logger + process-wide Diag facade
    Text/                    HtmlToFlowDocument, FlowDocumentToHtml, HtmlText (plain), CfHtml, ClipboardHtml
    Templates/               TemplateEngine (built-ins + response includes + bounded script tokens)
    Scripts/                 strict definitions, restricted expression interpreter, catalog, trust ledger
    ImportExport/            LibraryPorter (export/import), LibraryBackup (rolling snapshots + restore)
  QuickReplyKit/             WPF app
    Views/                   Launcher, Editor, Settings, AssignHotkey, HotkeyManager, ScriptManager, ImportPreview,
                             Backup, Trash, About, Onboarding, Input/Confirm dialogs, HighlightText
    ViewModels/              LauncherViewModel, ResponseRowVm, NavNodeVm, RelayCommand, ViewModelBase,
                             ImportCoordinator, ImportReviewModel, ScriptManagerModel
    Interop/                 HotkeyRegistrar (many hotkeys, one msg-only window), HotkeyGesture, HotkeyConflicts (pure
                             dup/lookup logic), ForegroundWindow, ForegroundMonitor, ForegroundTargetTracker,
                             ClipboardWriter, ClipboardBackup, InputSender
    Services/                InsertService, IInsertService, HotkeyService (owns launcher + per-response binds, instant
                             paste), ThemeManager, StartupRegistration
    Themes/                  Light.xaml, Dark.xaml (DynamicResource brushes)
    App.xaml(.cs)            startup, single-instance, tray, hotkey, async initial scan; SelfTest.cs (--selftest-* gates)
  QuickReplyKit.Tests/       xUnit over Core, view-models, app services, and Win32 interop
tools/                       shoot.ps1 (launch+screenshot+kill), capture-window.ps1, make-icon.ps1
assets/DefaultLibrary/       optional, independently removable seed responses and script examples
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
- Legacy libraries may contain `.qrkfolder.json` files from the retired folder-tag feature. They are inert: scanning,
  watching, and importing ignore them, while direct folder copying/export and folder Trash may carry them as opaque
  side files. QuickReplyKit never reads, writes, migrates, or deletes them on their own.
- **App config**: `%APPDATA%\QuickReplyKit\config.json` (launcher hotkey, library path, backup path, paste mode, window bounds,
  theme, remember-window, verbose logging, Trash retention, onboarding flag, and **per-response instant-paste
  hotkeys** keyed by library-relative path). **Recent** is `%APPDATA%\QuickReplyKit\recent.json`; Favorites remain
  in response files so they travel when shared.
- Script definitions use the strict portable `*.qrkscript.json` schema beside responses in the library; `language`
  must be `qrk-expr-1`. The scanner
  excludes them from responses/search/favorites/recent. Local approval fingerprints live only in
  `%APPDATA%\QuickReplyKit\script-trust.json`, cover the exact language and source, and never travel with a definition
  or export.
- Other local state: rolling logs in `%APPDATA%\QuickReplyKit\logs`, rolling library backups in
  `%USERPROFILE%\Documents\QuickReplyKit\Backups` by default, and recoverable deleted responses in
  `%APPDATA%\QuickReplyKit\trash`. None is scanned as library content.

## Rich text & templates
- **Canonical stored form = a restricted HTML subset**: bold, italic, underline, **strikethrough**, ul/ol/li,
  hyperlink, paragraphs — plus **font size/heading, text color, highlight, paragraph alignment** (added
  additively via inline styles/simple tags; **older files still load**). Converters in `Core/Text`:
  `HtmlToFlowDocument` (render/edit), `FlowDocumentToHtml` (serialize), `HtmlText.ToPlainText` (plain), and
  `CfHtml` (CF_HTML clipboard payload with a byte-exact StartHTML/EndHTML/StartFragment/EndFragment header).
  The editor parser copies supported formatting onto text runs. The serializer carries locally-set formatting from
  WPF paragraphs, lists, and list items into their runs before applying inline overrides. This normalizes both toolbar
  formatting and Word-originated RTF, whose inherited formatting often lives on a block rather than on each run.
- **New from Clipboard** prefers RTF and normalizes it through WPF's native RTF reader plus the shared serializer.
  When RTF is unavailable, CF_HTML routes through `ClipboardHtml.Sanitize`: active, hidden, and non-content blocks are
  removed before normalization. Hyperlinks are limited to `http`, `https`, and `mailto`; plain clipboard text is
  HTML-encoded before editing.
- **Templates** (`Core/Templates/TemplateEngine`): content may contain `{{…}}` tokens resolved at insert/preview/copy:
  built-in variables (`{{greeting}}`, `{{date}}`, `{{time}}`, `{{day}}`, `{{date:<fmt>}}`) and **includes**
  (`{{Response Name}}` splices another response's resolved HTML). Cycle detection, a fan-out expansion budget, and
  inline `[missing:…]`/`[cycle:…]`/`[includes: too many]` markers.
- **Restricted scripts** use `{{script:Name}}`. `qrk-expr-1` is a deliberately non-general-purpose interpreter with
  literals, fixed clock values, comparisons/boolean operators, and a closed set of pure string/date functions. It has
  no assignment, loops, user functions, reflection, filesystem, process, environment, registry, network, clipboard,
  UI, native loading, regex, or dynamic-evaluation capability. Definitions, parsing, AST depth/count, evaluation,
  arguments, literals, output, aggregate calls (64), and resolved content (250,000 chars) are bounded. Substitution
  occurs only in HTML text nodes, never tags or attributes. Invalid, disabled, ambiguous, missing, changed, or untrusted
  scripts fail closed with visible markers. All render paths use one captured clock.
- `ScriptTrustLedger` stores only local ID/source fingerprints using bounded strict JSON, atomic writes, and a named
  mutex with reload-before-mutate semantics. Language or source changes revoke trust. The Script Manager creates only
  restricted definitions, binds Trust to the exact source displayed to the user, and protects unsaved drafts with
  Save/Discard/Cancel.
- Preview, launcher paste/plain/copy, edit-before-insert, and instant hotkeys use the same synchronous bounded
  resolver. `TemplateResolutionResult` preserves safe script failures separately from preview HTML. Insert/copy
  actions stop before the clipboard, Recent update, or launcher hide when any script fails; launcher actions raise a
  themed dialog and instant hotkeys raise a tray notification. Resolution is bounded and has no operating-system or
  network access, so it does not require worker processes, cancellation, or preview-result caching.
- `assets/DefaultLibrary/Examples` demonstrates every built-in token, named response includes, and `qrk-expr-1`
  without coupling normal support examples to the demos. Portable definitions contain no approval state and remain
  inert until the user reviews each exact source and creates a local source-bound trust record in Script Manager.

## Paste engine (the core value) — `Interop/*` + `Services/InsertService`
- A `ForegroundMonitor` (WinEvent `EVENT_SYSTEM_FOREGROUND`, `WINEVENT_SKIPOWNPROCESS`) continuously records the
  last real (non-QuickReplyKit) foreground window into a shared `ForegroundTargetTracker` — so paste targets the
  app the user was in no matter how the launcher is summoned (a show-time capture is a fallback).
- On insert: snapshot the clipboard (`ClipboardBackup`) → restore foreground to the target (`ForegroundWindow`,
  with the AttachThreadInput focus-restore trick) → write the response with **history exclusion** via raw Win32
  (`ClipboardWriter`: `Rich Text Format` → `HTML Format` → `CF_UNICODETEXT`, plus `ExcludeClipboardContentFromMonitorProcessing`,
  `CanIncludeInClipboardHistory=0`, `CanUploadToCloudClipboard=0`) → paste → after a short delay, restore the snapshot.
  Microsoft Word 2405+ defaults cross-application Ctrl+V to Merge Formatting, which intentionally discards direct
  size/color and related source formatting. For a captured `WINWORD` target, `WordPaste` therefore invokes Word's own
  `Selection.PasteAndFormat(wdFormatOriginalFormatting)` through the installed Word COM automation surface; failure
  safely falls back to `SendInput` Ctrl+V. Other targets always use `InputSender`. Plain paste = text only (+ exclusion
  + restore). **Copy** = a normal clipboard set (user-intended; no exclusion/restore), so a later manual paste follows
  the destination application's own paste policy. Templates are resolved before building the payload.
- No mouse/keyboard hooks — only `RegisterHotKey` — so other apps' right-click menus are never affected.

## Windows/UI behaviour
- Settings uses a scrolling content region with a fixed action footer; About uses a stretchable diagnostics pane and
  wrapping actions. Both dialogs are resizable and capped to the available work-area height so controls remain
  reachable on short or high-scale displays.
- Single instance; system tray (Open / Open Library / Import / Export / Settings / Exit). Global hotkey via
  `RegisterHotKey` (default **Ctrl+Alt+D**, configurable). Launcher **hides** (not closes) on Esc/focus-loss/insert.
- Compact, resizable, keyboard-first launcher (3 panes: nav / results / preview; nav auto-collapses when narrow).
  Preview actions wrap at compact widths; icon targets keep stable usable dimensions without boxed pointer chrome.
  The active nav item uses the native system Highlight/HighlightText background and foreground plus a semibold label,
  so scope remains clear in High Contrast and without relying on color alone.
  Window size/position + monitor remembered and always re-opened on-screen (`WindowPlacement.EnsureVisible`).
- The icon-only **⇄** toolbar menu exposes response import and full-library export; its tooltip and automation name
  state the complete purpose. Exporting one response
  or one folder remains in the corresponding result/folder context menu, so each command's scope is explicit without
  duplicating actions across menus.
- Search ANDs whitespace tokens across name, response tags, app, category, and body. A selected category supplies the
  candidate set before ranking, limited to that folder subtree; Favorites and Recent likewise search only their own
  collections, while All Responses is global. The removable category chip shows the selected folder's leaf title,
  retains the complete category path internally, and preserves the query when cleared. Typo
  fallback uses bounded per-word edit distance within the active candidate set, with subsequence matching only for
  short abbreviations; literal body matches keep their snippets and long tokens never join across words.
- **Startup is non-blocking:** the window shows immediately in a "Loading library…" state; the initial scan runs
  on a background task (parallelized) and populates via the watcher's `Updated`→Dispatcher path.
- **Theming:** `Themes/Light.xaml`/`Dark.xaml` swapped at runtime by `ThemeManager`; all views use `DynamicResource`.
  `AppConfig.Theme` = System/Light/Dark (System follows Windows). `App.xaml` owns one shared button template whose
  translucent hover/pressed layer preserves each button's base color; all view-local button styles inherit it.
  Per-Monitor-v2 DPI is enabled via `app.manifest`.
- The editor uses a themed ComboBox template, standard text/highlight palettes with visual swatches, removable
  response-tag chips, and a visible launcher Edit action. Formatting controls expose checked/mixed state at the caret
  or selection; High Contrast pairs native Highlight and HighlightText resources. Error text and actions occupy
  separate responsive columns.
- Import uses one immutable `ImportPlan` from `LibraryPorter.PlanImport`; the preview displays that exact plan and
  `LibraryPorter.Import(plan, decisions)` executes it. Entries are classified as New, Duplicate elsewhere, Identical,
  Conflict, Invalid, or Unsafe. New/duplicate/conflict rows use explicit checkboxes; conflicts expose only per-row
  Overwrite/Keep both choices, with Keep both as the safe selection default. Identical/Invalid/Unsafe rows are locked
  to Skip. Bulk selection excludes entries marked for mandatory individual approval. Planning validates canonical containment before destination reads,
  ignores legacy `.qrkfolder.json`, rejects path aliases/reparse traversal and bounded-ZIP violations, and records
  source/destination fingerprints. Execution rechecks those fingerprints, reports stale entries without writing them,
  and atomically replaces individual files. Valid siblings continue when another entry is invalid, unsafe, stale, or
  fails. A required rolling backup is created after the user accepts the plan and before any write.
- Portable restricted script definitions participate in import/export but trust never does. Script rows show exact
  source, begin unchecked, and cannot be selected by Select all. Each exact plan entry needs a separate user acceptance;
  Core execution independently rejects unaccepted code. An accepted script is trusted only after its destination is
  written, reloaded, and byte-verified. Stale, unsafe, invalid, or failed writes remain untrusted.
- `LibraryBackup` writes a versioned ZIP containing `library/`, `config.json` when present, and a manifest. Retention
  keeps at most 10 snapshots within count, 500 MB, and 90-day bounds while preserving at least the newest. Restore
  first protects the selected archive, validates and size-bounds extraction beside the live library, snapshots the current state,
  then swaps directories. A failed install rolls the exact original directory back; older library-only ZIPs remain
  restorable without changing settings. Restored config is retargeted to the actual restored library folder; the old
  launcher's final config save is suppressed and QuickReplyKit restarts so restored settings become authoritative.
  `AppConfig.BackupPath` selects the snapshot directory (default `AppPaths.BackupsDir`). `BackupLocation` requires an
  absolute path outside the response library and verifies write access before Settings saves it and before
  create/list/restore operations. An unavailable configured path therefore blocks only the affected safety operation,
  never normal startup.
  `TrashService` soft-deletes individual responses and complete category subtrees. Folder deletion validates the
  library tree, requires a `LibraryBackup` snapshot, and moves every nested response/side file as one typed Trash
  payload. Old response-only metadata remains readable. Restore recreates the exact original hierarchy, refuses
  overwrite/path escape/reparse traversal, and leaves conflicts in Trash; delete-forever and startup retention purge
  apply to both item types.
- `Logger` uses one background writer, level gating, source/category metadata, and size rollover (about 10 MB x 5
  files). Normal events summarize lifecycle, scans, import/export, watcher state, themes, hotkeys, and paste outcomes;
  verbose events add bounded stage/count/timing detail. Crash records retain exception types, HRESULTs, and path-free
  method frames. Logs exclude queries, response/template/clipboard content, hotkey values, HWNDs, paths, and exception
  messages; the sink also flattens control characters to prevent forged log lines.
- First launch schedules a three-card onboarding dialog after persisting `OnboardingSeen`. About/Diagnostics is a
  copyable, read-only support snapshot built by `DiagnosticsReport`: runtime/version, aggregate actual hotkey
  registration, library/watcher/logger state, counts, and resolved filesystem paths. It never includes response
  details, individual response hotkeys, script source, or clipboard content. Settings links to hotkeys, backups,
  Trash, logs, and diagnostics.
- Accessibility is defined in XAML at the control boundary: Automation names/help, polite/assertive live regions,
  native keyboard menus, safe dialog defaults, keyboard-visible focus, full-value tooltips for clipped text, and
  theme/High Contrast resources. Collection dialogs such as Backup and Trash resize while keeping actions reachable.

## Known completion boundaries

The implemented product paths above are merged and tested through B34 plus B36-B39. [`docs/STATUS.md`](docs/STATUS.md)
records the exact audited baseline, and [`docs/BACKLOG.md`](docs/BACKLOG.md) contains unfinished requested work only.
B35's first RTF release is on `main`, but real-Word acceptance rejected it. The corrective branch and remaining gate
are recorded in STATUS/BACKLOG; do not close B35 without a repeat test in Microsoft Word.

## Validation
- `dotnet build` clean + `dotnet test` green; the paste engine has E2E self-tests (`--selftest-paste`, `-rich`,
  `-ui`, `-foreground`). Target-based gates launch an owned child QuickReplyKit process with a native edit control,
  then close that exact process; they never discover or modify user Notepad windows.
  they use native Ctrl+V when foreground activation succeeds and a test-only `WM_PASTE` sender when Windows correctly
  denies background automation foreground input, while preserving the real clipboard and command paths. UI changes
  also use isolated app launches, repository screenshot helpers, and direct visual
  review without GPT Computer Use/UIAutomation or interference with unrelated windows. Two manual spot-checks remain:
  a live hotkey paste into a third-party app,
  and confirming pasted text is absent from Win+V history.
