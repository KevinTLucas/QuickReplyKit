# QuickReplyKit — Project Status (living handoff)

> **Single source of truth for "where we are." A new session or a different AI should be able to read
> [`../CLAUDE.md`](../CLAUDE.md) + this file + [`ROADMAP.md`](ROADMAP.md) and continue with zero prior context.**
> **Update this file at the end of every meaningful change** (it is part of the Definition of Done — see CLAUDE.md).

- **Last updated:** 2026-09-06
- **`main` tip:** `71dc402` · **`dotnet test` = 384 passing**, `dotnet build` clean (0 warnings).
- **Overall status: ✅ MVP + all V2 features complete and QA'd. Now in a continuous end-user polish pass**
  (the CI-N backlog below). The app is fully usable, launchable, and portable today.
- **Run:** `dotnet run --project src/QuickReplyKit -- --show` (also runs to tray). Global hotkey **Ctrl+Alt+D**.
- **Self-tests (all pass):** `--selftest-paste` (PASTE=ok CLIPBOARD_RESTORED=ok), `--selftest-paste-rich`
  (RICHPASTE=ok), `--selftest-paste-ui` (UIPASTE=ok), `--selftest-foreground` (FOREGROUND_TRACK=ok).

---

## What is DONE (all merged to `main`)
**MVP (phases 0–7):** launcher (3-pane nav/results/preview, live search, keyboard nav, favorites, recent),
paste engine (global hotkey → focus-restore → clipboard-safe insert with history-exclusion; `ForegroundMonitor`
tracks the true target window), rich-text editor + full response management (create/edit/duplicate/rename/move/
delete-to-Recycle-Bin, new-from-clipboard, open-in-Explorer), import/export (conflict resolution, invalid-tolerant,
zip path-traversal-safe), settings (hotkey/library-path/default-paste/launch-at-startup, persisted), tray,
window memory + multi-monitor, portable single-file packaging, README.
**V2 (all merged):**
- **Phase R** — lenient per-field JSON parsing (one bad field no longer invalidates a response) + watcher post-dispose guard.
- **Phase 8** — full-UI **light/dark theming** (System/Light/Dark, follows Windows; `Themes/Light.xaml`+`Dark.xaml`,
  `Services/ThemeManager.cs`, `Core/Storage/ThemeResolver.cs`; Settings Appearance toggle).
- **Phase 9** — **template engine** (`Core/Templates/TemplateEngine.cs`): allow-listed built-ins
  `{{greeting}}/{{date}}/{{time}}/{{day}}/{{date:fmt}}` + **response includes** `{{Name}}` (cycle detection,
  missing/`[cycle]`/`[includes: too many]` markers, `{{{{` escape). Resolved at insert/preview/copy. **No code execution.**
- **Phase 10** — **richer editor** (font size/heading, text color, highlight, alignment, strikethrough; additive HTML
  subset, old files still load) + **edit-before-insert** (right-click → "Edit & insert…" opens a resolved, editable
  compose window; Insert pastes without saving a file).

Full per-phase detail + pickup instructions: [`ROADMAP.md`](ROADMAP.md). V2 phase specs: [`V2-PLAN.md`](V2-PLAN.md).

---

## 🔧 OPEN WORK — continuous-improvement backlog (what's LEFT)
From the end-user UX review ([`UX-REVIEW.md`](UX-REVIEW.md) has full detail/rationale/measurements). Do in value order,
each on a branch → impl (ponytail) → separate-subagent QA → merge `--no-ff` when green → update this file.

- **CI-1 · Performance (highest value; BUG-class).** Large-library startup blocks the UI ~19s (synchronous scan on the
  UI thread). Fix: (a) **parallelize** `LibraryScanner.Scan` file loads (measured ~3.6× faster; I/O-bound via AV/OneDrive);
  (b) **don't scan on the UI thread** — show the launcher immediately with a "Loading…" state, populate on a background
  task (watcher already marshals updates); (c) **H1**: stop eagerly calling `File.GetCreationTimeUtc`+`GetLastWriteTimeUtc`
  per file in `ResponseFile.Load` (~30% of scan; `Created`/`Modified` are never displayed/used). Also speeds `LibraryService.Reload()`.
  Files: `Core/Library/LibraryScanner.cs`, `Core/Storage/ResponseFile.cs`, `Core/LibraryService.cs`, `App.xaml.cs`, VM loading state.
- **CI-2 · Search quality.** **H2** highlight matched substring in the name + snippet when the hit is body-only;
  **M1** lazy, dependency-free typo tolerance (edit-distance ≤1 / subsequence on name+tags, only when a token has 0 exact
  matches); **M2** rank in-scope (selected-folder) hits first; **M4** let search match invalid files by name (still non-pasteable).
  Files: `Core/Search/ResponseSearch.cs`, `ViewModels/LauncherViewModel.cs`, `ViewModels/ResponseRowVm.cs`, `Views/LauncherWindow.xaml`.
- **CI-3 · Interaction polish.** **M3** "Copied ✓" feedback on Copy; **L3** `AutomationProperties.Name` on icon-only
  buttons (screen-reader); **L5** guard `LibraryWatcher.Start`'s `Directory.CreateDirectory` so an offline OneDrive path
  isn't recreated locally. (Optional: a tiny keyboard-shortcut hint.)
- **Considered, NOT doing** (scope/low value): natural-sort, number-to-pick, dark-combobox chrome (cosmetic). See UX-REVIEW.

**Stop condition for the loop:** when no obvious high-value improvement remains and the app feels complete/polished/reliable,
run a final full regression (build + 4 self-tests + tests) and STOP.

## Known issues / caveats (non-blocking)
- **Large-library startup latency** — the CI-1 item above; the one real weakness today.
- Editable rename/category **ComboBox** closed field keeps native light chrome in dark mode (text readable). Cosmetic.
- A **highlight** color with no explicit text color can be low-contrast in dark mode (same as Word/Outlook). Documented.
- **Phase 4 UI** never had a *dedicated* independent QA (its Core did; the Phase-7 + combined QA swept it — no defects). Fine.
- `--selftest-paste-rich` proves the clipboard carries valid **CF_HTML** (what Outlook/Word/browsers render), not an in-app
  render (Win11 Msftedit has no HTML importer).
- **2 human spot-checks** (un-automatable): live Ctrl+Alt+D paste into a 3rd-party app; confirm pasted text absent from Win+V history.
- Deferred robustness + the **V2 idea list beyond what's built** live in [`BACKLOG.md`](BACKLOG.md).

## How to resume in a fresh session
1. Read `CLAUDE.md`, this file, then `ROADMAP.md` (and `UX-REVIEW.md` for the CI backlog detail).
2. `export PATH="/c/Program Files/dotnet:$PATH"; dotnet build QuickReplyKit.slnx && dotnet test QuickReplyKit.slnx` → expect **384** green.
3. Pick the top open **CI-N** item. Branch off `main` (worktree), implement with ponytail, validate, QA via a **separate**
   subagent (build + tests + both-theme screenshots + the 4 self-tests + back-compat), merge `--no-ff`, delete branch, **update this file**.
