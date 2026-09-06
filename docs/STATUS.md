# QuickReplyKit — Project Status (living handoff)

> **Single source of truth for "where we are." A new session or a different AI should be able to read
> [`../CLAUDE.md`](../CLAUDE.md) + this file + [`ROADMAP.md`](ROADMAP.md) and continue with zero prior context.**
> **Update this file at the end of every meaningful change** (it is part of the Definition of Done — see CLAUDE.md).

- **Last updated:** 2026-09-06
- **`main` tip:** `53a1333` · **`dotnet test` = 403 passing**, `dotnet build` clean (0 warnings).
- **Overall status: ✅ COMPLETE — MVP + all V2 features + the continuous-improvement pass (CI-1/2/3) all done and merged.**
  The app is polished, reliable, launchable, and portable. No obvious high-value improvement remains open (the UX review's
  remaining items are low-value/scope-creep — see UX-REVIEW.md). Final regression green (build + 403 tests + all 4 self-tests).
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

- **CI-1 · Performance (highest value; BUG-class). ✅ DONE — merged to `main` (`864db1a`).**
  (Reviewed: async-startup path correct/thread-safe; determinism proven vs a sequential oracle; 386 tests + 4 self-tests
  green. Screenshots weren't capturable this session — the "Loading library…" visual + dark-mode highlight contrast
  warrant a human glance when convenient; folded into the final regression pass.)
  All three fixes landed: (a) `LibraryScanner.Scan` now enumerates the tree first and loads files with `Parallel.For`
  (pure/stateless `ResponseFile.Load`), reassembling in the original pre-order so the snapshot is byte-for-byte identical
  (same items/order/invalid handling); (b) startup no longer scans on the UI thread — `LibraryService` takes
  `deferInitialScan`, `App` shows the launcher immediately in a **"Loading library…"** state and runs the first scan on a
  background `Task` (`LoadInitial()` → `Updated` → Dispatcher, the same path the watcher uses); (c) **H1**: `ResponseFile.Load`
  no longer calls `File.GetCreation/LastWriteTimeUtc` per file — `Created`/`Modified` stay `default` when absent (never
  displayed/used; `Save` still fills `Created`). Local benchmark (~4.8k files, warm cache, no AV/OneDrive drivers):
  parallel+lazy **181 ms** vs sequential+eager **532 ms** (~2.9×; eager timestamps were ~19% of the sequential scan) — bigger
  on real hardware where filter-driver I/O dominates. **386 tests green** (added: parallel-scan-determinism vs sequential
  oracle incl. nested/invalid files; deferred-load populate+Updated; H1 leaves timestamps default & still valid; 2 old
  file-time-fallback tests updated to new behavior). All 4 self-tests pass. NOTE: screenshots couldn't be captured in this
  automated session (WPF GPU-composited content isn't readable via `CopyFromScreen` here — pre-change `main` captures blank too);
  UI behavior is covered by the VM/deferred-load tests and needs a human/interactive screenshot pass at QA.
  Files: `Core/Library/LibraryScanner.cs`, `Core/Storage/ResponseFile.cs`, `Core/LibraryService.cs`, `App.xaml.cs`, `ViewModels/LauncherViewModel.cs`.
- **CI-2 · Search quality. ✅ DONE — merged to `main` (`4d8cee3`, 400 tests).** H2 match highlighting (accent-bold via
  `Views/HighlightText.cs`, theme-safe) + body-only snippet; M1 lazy fuzzy fallback (subsequence / Levenshtein ≤1–2 on
  name+tags, tier `Fuzzy`, only when a token has 0 exact matches — common path unregressed); M2 in-scope (selected folder)
  ranked first; M4 invalid files matched by name (ranked below valid, still non-pasteable). `ResponseSearch.Rank`→`SearchHit`.
- **CI-3 · Interaction polish. ✅ DONE — merged to `main` (`53a1333`, 403 tests).** M3 "Copied ✓" feedback (auto-clears
  after 1.5s); L3 `AutomationProperties.Name` on the icon-only buttons + ★ toggle (screen-reader); L5 `LibraryWatcher.Start`
  no longer fabricates the library folder when its parent is missing (offline OneDrive safe), skips watching gracefully.
- **Considered, NOT doing** (scope/low value): natural-sort, number-to-pick, dark-combobox chrome (cosmetic). See UX-REVIEW.

**Continuous-improvement pass: ✅ COMPLETE.** All CI items merged; final regression green. The loop is stopped. Any further
work would be new feature requests (see BACKLOG.md for longer-term ideas) — start those only when the user asks.

## Known issues / caveats (non-blocking)
- **Large-library startup latency** — ✅ fixed by CI-1 (merged): scan is now parallel, lazy on timestamps, and off
  the UI thread (window shows immediately with "Loading library…").
- Editable rename/category **ComboBox** closed field keeps native light chrome in dark mode (text readable). Cosmetic.
- A **highlight** color with no explicit text color can be low-contrast in dark mode (same as Word/Outlook). Documented.
- **Phase 4 UI** never had a *dedicated* independent QA (its Core did; the Phase-7 + combined QA swept it — no defects). Fine.
- `--selftest-paste-rich` proves the clipboard carries valid **CF_HTML** (what Outlook/Word/browsers render), not an in-app
  render (Win11 Msftedit has no HTML importer).
- **2 human spot-checks** (un-automatable): live Ctrl+Alt+D paste into a 3rd-party app; confirm pasted text absent from Win+V history.
- Deferred robustness + the **V2 idea list beyond what's built** live in [`BACKLOG.md`](BACKLOG.md).

## How to resume in a fresh session
1. Read `CLAUDE.md`, this file, then `ROADMAP.md` (and `UX-REVIEW.md` for the CI backlog detail).
2. `export PATH="/c/Program Files/dotnet:$PATH"; dotnet build QuickReplyKit.slnx && dotnet test QuickReplyKit.slnx` → expect **386** green.
3. Pick the top open **CI-N** item. Branch off `main` (worktree), implement with ponytail, validate, QA via a **separate**
   subagent (build + tests + both-theme screenshots + the 4 self-tests + back-compat), merge `--no-ff`, delete branch, **update this file**.
