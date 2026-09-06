# QuickReplyKit — Full Phase Roadmap

The complete plan, every phase, done and remaining, with enough detail for a fresh agent to pick up
any remaining phase and work it. **For the current snapshot + the ordered next action, see
[`STATUS.md`](STATUS.md).** For how to build/test/run and the conventions, see [`../CLAUDE.md`](../CLAUDE.md).

Legend: ✅ done & merged · ⏳ partially done · ☐ not started.
Each remaining item lists: **Spec**, **Branch to create**, **Validation gate**, **QA** (a separate
subagent must review before merge). Standard flow for every phase: branch off `main` → implement with
ponytail → build/test/screenshot/self-test green → independent QA subagent → fix → merge `--no-ff` →
delete branch → update `STATUS.md`.

---

## ✅ Phase 0 — Skeleton  (merged)
Single-instance app, system tray (`NotifyIcon`), generated `app.ico`, Per-Monitor-v2 DPI manifest,
`--show` flag, screenshot tooling (`tools/shoot.ps1`, `capture-window.ps1`, `make-icon.ps1`).
Code: `src/QuickReplyKit/App.xaml.cs`, `Views/LauncherWindow.*`.

## ✅ Phase 1 — Headless Core  (merged, QA'd — 99 tests)
Model (`ResponseItem`/`CategoryNode`/`LibrarySnapshot`), recursive `LibraryScanner`, live
`LibraryWatcher` (debounced), ranked `ResponseSearch`, `HtmlText.ToPlainText`, `CfHtml.Build`
(byte-exact CF_HTML), `AppConfig`/`RecentStore`, first-run `LibrarySeeder`. Spec: `ARCHITECTURE.md`.
Code: `src/QuickReplyKit.Core/**`.

## ✅ Phase 2 — Launcher UI  (merged, QA'd — 142 tests)
Three resizable panes (nav / results / preview) w/ auto-collapsing nav, live search, keyboard nav
(type→arrows→Enter, single-click=preview-only, double-click=insert), rich read-only preview
(`HtmlToFlowDocument`), favorites, `LibraryService`, action bar → `IInsertService`. Spec:
`PHASE2-LAUNCHER.md`. Code: `src/QuickReplyKit/ViewModels/**`, `Views/LauncherWindow.*`,
`Core/LibraryService.cs`, `Core/Text/HtmlToFlowDocument.cs`.

## ✅ Phase 3 — Paste engine  (merged, QA'd — 224 tests)
Global hotkey (`RegisterHotKey` + gesture parser), foreground capture + restore (dual
`AttachThreadInput`), raw-Win32 clipboard write with **history/cloud exclusion formats**, clipboard
snapshot/restore, `SendInput` Ctrl+V, `InsertService`, `--selftest-paste`. History-exclusion is
unit-proven. Spec: `PHASE3-PASTE.md`. Code: `src/QuickReplyKit/Interop/**`, `Services/InsertService.cs`.
> ✅ The real-app target-capture bug is FIXED (`ForegroundMonitor`, `main` f8a5b1d) — awaiting the
> user's live hotkey confirmation. See `STATUS.md`.

## ✅ Phase 4 — Editor + response management  (merged; Core QA'd — 236 tests)
Rich-text `EditorWindow` (B/I/U, bullet/numbered lists, link, remove-formatting; Name/Category/App/
Tags; category picker that creates new nested folders), `FlowDocumentToHtml`, `ResponseManager`
(create/duplicate/rename/move/delete→Recycle Bin/createFolder), results context menu, New +
New-from-clipboard, confirm/input dialogs. Spec: `PHASE4-EDITOR.md`. Code: `Views/EditorWindow.*`,
`Views/InputDialog.*`, `Views/ConfirmDialog.*`, `Core/Storage/ResponseManager.cs`,
`Core/Text/FlowDocumentToHtml.cs`, `Core/Text/ClipboardHtml.cs`.
> Note: the **UI** half hasn't had a dedicated independent-QA pass (Core has). Worth a QA sweep.

---

## ⏳ Phase 5 — Import / Export  (Core ✅ merged · UI ☐)
**Done:** `QuickReplyKit.Core.ImportExport.LibraryPorter` — `Export`(one json), `ExportFolder`(zip),
`ExportLibrary`(zip), `Import(...) → ImportResult` with `ConflictPolicy {Overwrite,KeepBoth,Skip}`,
invalid-tolerant. (10 tests, on `main`.)
**Remaining (UI):** wire export (results menu → Export response…; nav category → Export folder…;
top-bar/tray → Export library…) and import (Open dialog `.json`/`.zip` → pick target category →
conflict prompt reusing `ConfirmDialog` as 3-way → summary). Watcher auto-refreshes after.
- **Spec:** `PHASE5-IMPORT-EXPORT.md`  ·  **Branch:** `phase-5-ui`
- **Validation gate:** build/test green; a test (or `--selftest-importexport` hook, removed before
  commit) that export→delete→re-import restores an identical hierarchy; a zip with one broken file
  still imports the rest; screenshot the target-category + conflict + summary dialogs and Read them.
- **QA:** separate subagent — conflict-policy correctness, invalid-tolerance, dialog UX, ponytail-review.

## ☐ Phase 6 — Settings, window memory, tray  (not started; Core clamp logic drafted only)
Settings window (global-hotkey capture control → `GlobalHotkey.Rebind`; library-path picker; default
paste mode; remember-window toggle; optional launch-at-startup via HKCU Run); window size/position +
monitor memory with **"always reopen visible"** (clamp saved rect to a current work area, else
re-center); tray **Settings** wiring; hotkey-registration-failure balloon points here.
- **Spec:** `PHASE6-SETTINGS-WINDOW.md`  ·  **Branch:** `phase-6-settings` (+ optional parallel
  `phase-6-core` worktree for the pure `WindowPlacement.EnsureVisible` clamp function + tests).
- **Validation gate:** build/test green (AppConfig persistence incl. `WindowBounds`; the off-screen
  clamp is a pure, unit-testable function — test it with fake screen rects); screenshot the Settings
  window; verify hotkey capture records a combo; verify a saved off-screen position re-centers;
  confirm prefs persist across restart; test at 100/150/200% DPI.
- **QA:** separate subagent — multi-monitor correctness, hotkey rebind edge cases, persistence.

## ☐ Phase 7 — Polish, packaging, final verification  (not started)
See **`PHASE7-POLISH-PACKAGING.md`**. Rich-paste E2E, full-workflow regression, empty/edge-state
sweep, portable single-file `dotnet publish`, README + user docs, performance check on a large library.
- **Branch:** `phase-7-polish` · **Gate/QA:** full-workflow regression must pass; see that spec.

---

## What's left, in one line
**Fix the paste target bug (top of STATUS.md) → Phase 5 UI → Phase 6 → Phase 7.** Everything through
Phase 4 + the Core of Phase 5 is done and on `main` (236 tests, build clean).
