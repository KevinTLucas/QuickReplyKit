# Phase 2 — Launcher Window (UI + keyboard + live search)

Goal: the compact, fast launcher a user sees after the hotkey. Browsing, searching,
previewing, and the three insert actions (Paste / Paste plain / Copy). No paste **injection**
yet (that's Phase 3) — wire the buttons to a `IInsertService` seam that Phase 3 implements;
for Phase 2 a stub that copies to clipboard + logs is fine so the flow is testable.

## Layout (three resizable columns + search on top)
```
┌───────────────────────────────────────────────────────────┐
│ 🔍  [ search box …………………………………………………… ]   ⚙ (settings)     │  top bar
├──────────┬───────────────────────┬────────────────────────┤
│ NAV      │ RESULTS               │ PREVIEW                 │
│ ⭐ Favs  │  ▸ No Campus License  │  (rich, read-only)      │
│ 🕘 Recent│    IXL · Licensing    │                         │
│ ▾ Clever │  ▸ License Expired    │  ─────────────────────  │
│   ▾ IXL  │    IXL · Licensing    │  App: IXL  Tags: …      │
│     Lic… │  ⚠ Broken.json        │  [Paste] [Plain] [Copy] │
│ ▸ Outlook│                       │                         │
└──────────┴───────────────────────┴────────────────────────┘
```
- `Grid` with 3 columns separated by `GridSplitter`s. Each column has a sensible `MinWidth`
  (nav ~140, results ~200, preview ~220). Window `MinWidth≈560`, `MinHeight≈360`, default
  **~780×520** (small, since it opens over other apps). Controls must never overlap when resized —
  rely on min-widths + splitters, no absolute positioning.
- **Nav collapse:** a toggle (and/or auto-collapse under ~620px width) hides the nav column to
  reclaim space; results+preview remain fully usable.
- Everything scales with WPF layout + DPI (Per-Monitor v2 already in the manifest). Test at 100/150/200%.

## Nav pane
Tree with fixed top items then the category folder tree:
- **⭐ Favorites** — all responses with `Favorite=true`.
- **🕘 Recent** — from `RecentStore`, most-recent first.
- **All Responses** — flat list of every valid response.
- Category tree from `LibrarySnapshot.Root` — folders expand/collapse; selecting a folder shows
  its responses **including descendants**. Deep nesting must scroll, not overflow. Empty folders appear and are selectable.

## Results pane
- Bound to an `ObservableCollection<ResponseRowVm>`; **virtualized** (`VirtualizingStackPanel`,
  `ScrollViewer.CanContentScroll=true`) so 5000+ items stay smooth.
- Row: bold **Name**, subtitle = `CategoryPath` (or `App` when set), a right-aligned ⭐ toggle
  (click toggles favorite; must not trigger paste). Invalid files show a ⚠ glyph, muted, and are
  not paste-able (Paste/Plain/Copy disabled; preview shows the parse error).
- Long names/subtitles **truncate with ellipsis** (`TextTrimming`), full text in a tooltip.

## Search behaviour
- Search box is focused on open and after each show. Filtering is **live** (update on each
  keystroke; debounce a few ms only if needed — must feel instant).
- **Query empty** → results = the selected nav scope (Favorites / Recent / All / a category subtree).
- **Query non-empty** → global search over the whole library via `ResponseSearch.Search` (nav
  selection ignored), ranked. Show the category as subtitle so users see where each lives.
- Empty states (distinct, friendly, centered in the results pane):
  - Library empty → "No responses yet" + hint to add/import or open the library folder.
  - No search matches → "No responses match \"{query}\"".
  - Empty category → "No responses in this folder".

## Preview pane
- Renders the selected response's rich content **read-only** via a new
  `QuickReplyKit.Core.Text.HtmlToFlowDocument.Convert(html) -> FlowDocument` shown in a
  `FlowDocumentScrollViewer` (hide its toolbar; `IsToolBarVisible=False`). Supported subset only:
  p, br, b/strong, i/em, u, ul/ol/li, a (hyperlinks rendered, clickable via default browser).
- Below the document: small metadata line (App, Tags) and the action bar.
- Preview must reflow/scroll cleanly as the window resizes. For an invalid response, show the
  filename + the parse error instead of a document.

## Actions & keyboard
Action bar buttons: **Paste** (rich), **Paste as plain text**, **Copy**. All call into
`IInsertService` (Phase 3). Disabled when no valid response is selected.

Keyboard contract (this is a headline feature — get it right):
- Open → caret in search box.
- **Down/Up** (while focus in search box) move the results selection and keep it scrolled into
  view; focus stays in the search box so the user can keep typing.
- **Enter** = default insert on the selected response (`AppConfig.DefaultPasteMode`: Rich or Plain).
- **Shift+Enter** = Paste as plain text. **Ctrl+C** (when a result is selected and search box
  empty of selection intent) = Copy. Also expose these on the buttons' tooltips.
- **Esc** = hide the launcher.
- **Tab** moves focus search → results → preview → buttons in a sensible order.
- **Single click** a result = select + preview only (NEVER auto-insert). **Double-click** = default insert.
- Ctrl+L or Ctrl+F refocus search (nice-to-have).

## Wiring
- Add `QuickReplyKit.Core.LibraryService` (headless coordinator): holds current `LibrarySnapshot`,
  loads via `LibraryScanner`, watches via `LibraryWatcher` (raise an `Updated` event marshalled by
  the UI to the Dispatcher — the watcher fires on a thread-pool thread), exposes `Search(query)`,
  `ToggleFavorite(item)` (rewrites the file via `ResponseFile.Save`), `MarkUsed(item)` (RecentStore).
  On startup: `LibrarySeeder.SeedIfEmpty(config.LibraryPath, <BaseDir>/DefaultLibrary)`, then scan, then watch.
- MVVM-lite: hand-rolled `ViewModelBase : INotifyPropertyChanged` + a tiny `RelayCommand`. **No MVVM
  framework / no NuGet.** ViewModels: `LauncherViewModel`, `ResponseRowVm`, nav node VM.
- App wires the real `LauncherWindow` to the VM; keep `ShowLauncher()`/tray/single-instance from Phase 0.

## Validation gate (QA, separate agent)
- `dotnet build` clean, `dotnet test` green (add tests for `HtmlToFlowDocument` subset round-trip via
  STA, `LibraryService` search/favorite/recent behavior).
- Screenshot via `tools/shoot.ps1` at default size, tiny size, and large size; verify no overlap,
  ellipsis works, empty states show. Provide screenshots.
- Keyboard walkthrough: type → Down/Up → Enter path reaches `IInsertService` with the right item+mode.
- Live update: add/modify a `.json` under the library while open → results refresh without restart.
