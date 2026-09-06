# QuickReplyKit — Project Status (living handoff)

> **This is the single source of truth for "where we are."** Update it at the end of every work
> session. Read [`../CLAUDE.md`](../CLAUDE.md) first for how to build/test/run and the conventions,
> and [`ROADMAP.md`](ROADMAP.md) for the full phase plan (0–7) with per-phase pickup instructions.

- **Last updated:** 2026-09-05
- **Branch layout:** `main` only (always green). Feature work happens on `phase-N-*` branches.
- **`main` tip:** `f8a5b1d` — Merge fix-paste-target.
- **Health:** `dotnet build` clean (0 warnings) · `dotnet test` **236 passing** · `--selftest-paste` →
  `PASTE=ok CLIPBOARD_RESTORED=ok` · `--selftest-foreground` → `FOREGROUND_TRACK=ok`.
- **Launchable?** Yes: `dotnet run --project src/QuickReplyKit -- --show` (also runs to the tray).

---

## ✅ RECENTLY FIXED — paste targeted the wrong window (was user's #1 bug)
**Status: FIXED on `main` (`f8a5b1d`). Needs a real-world hotkey confirmation from the user.**

**Was:** the paste target was only sampled at show-time via `GetForegroundWindow()`, so a fresh
launch / tray-open captured the launching shell or the launcher itself instead of the user's app;
clicking Paste sent Ctrl+V to the wrong window.

**Fix:** `src/QuickReplyKit/Interop/ForegroundMonitor.cs` — a `SetWinEventHook`
(`EVENT_SYSTEM_FOREGROUND`, `WINEVENT_SKIPOWNPROCESS`) that continuously records the last
non-QuickReplyKit foreground window into the shared `ForegroundTargetTracker` that `InsertService`
reads. Wired in `App.xaml.cs` (created on the UI thread, disposed on exit); show-time capture kept as
a fallback. Proven by `--selftest-foreground` → `FOREGROUND_TRACK=ok`; paste + 236 tests still green.

**Remaining confirmation:** automated tests prove the tracker captures the target and the engine
pastes into a given target, but the live click-through-the-UI path isn't auto-driven. Ask the user to
verify the real workflow: focus an app (e.g. Notepad) → press the global hotkey **Ctrl+Alt+Space** →
pick a response → Enter → text lands at the caret. (A `--selftest-paste-ui` that drives the live
window is a nice-to-have for Phase 7 regression.)

---

## Phase progress
| Phase | Scope | Status | Tests |
|---|---|---|---|
| 0 | Skeleton: single-instance, tray, icon, DPI manifest, `--show`, screenshot tooling | ✅ merged | — |
| 1 | Core: model, scanner, live watcher, search, HTML→text, CF_HTML, settings/recent, seeding | ✅ merged + QA | 99 |
| 2 | Launcher UI: 3-pane nav/results/preview, live search, keyboard nav, favorites, action bar | ✅ merged + QA | 142 |
| 3 | **Paste engine**: global hotkey, focus restore, clipboard-safe insert, history-exclusion | ✅ merged + QA | 224 |
| 4 | Rich-text editor + response management (create/edit/duplicate/rename/move/delete, ctx menu) | ✅ merged (Core QA'd) | 236 |
| 5 | Import/Export: **Core done+merged (LibraryPorter)**; **UI NOT started** | ⏳ Core ✅ / UI ☐ | 236 |
| 6 | Settings window, window memory + multi-monitor, tray Settings | ☐ not started (Core clamp logic drafted only) | — |
| 7 | Polish, **rich-paste E2E**, portable single-file packaging, README | ☐ not started | — |

## Active work right now
**None running.** (Two subagents — Phase 5 UI and Phase 6 Core window-placement — were killed early
by a session rate limit and committed **nothing**; the tree is clean at `main`. Re-launch them fresh.)

## Needs QA / known gaps
- Paste target fix (above) — needs the user's live hotkey confirmation; consider an independent QA
  pass + a `--selftest-paste-ui` live-window regression test.
- **Rich-paste E2E is unproven** — self-test only pastes *plain* into Notepad. Add a rich (CF_HTML)
  paste check (WordPad is gone on Win11 24H2; use Word if present, a Win32 RichEdit harness, or an
  in-process RichTextBox) — see BACKLOG. Do this in Phase 7 final verification.
- History-exclusion is proven at the API level (formats set) but **not** confirmed absent from the
  real Win+V UI / Cloud Clipboard — needs a human spot-check.
- Deferred robustness items: [`BACKLOG.md`](BACKLOG.md) (root-level invalid files not surfaced;
  strict per-field JSON parsing; empty-folder-in-empty-library message).

## Immediate next actions (in order)
1. **(done — awaiting user's live hotkey confirmation)** paste target fix, see the FIXED section above.
2. **Phase 5 UI** — import/export menus + conflict dialog + summary, on `phase-5-ui`. Core
   (`QuickReplyKit.Core.ImportExport.LibraryPorter`) already exists on `main`. Spec:
   [`PHASE5-IMPORT-EXPORT.md`](PHASE5-IMPORT-EXPORT.md).
3. **Phase 6** — Settings window (hotkey capture, library path, default paste, launch-at-startup),
   window size/position memory + multi-monitor "always reopen visible", tray Settings. Spec:
   [`PHASE6-SETTINGS-WINDOW.md`](PHASE6-SETTINGS-WINDOW.md). (Window-placement clamp logic can be a
   parallel Core task in a worktree.)
4. **Phase 7** — polish, rich-paste E2E, `dotnet publish` self-contained single-file portable exe,
   README + user-facing docs, full-workflow regression.

## How to resume in a fresh session
1. Read `CLAUDE.md` then this file.
2. `export PATH="/c/Program Files/dotnet:$PATH"; dotnet build QuickReplyKit.slnx && dotnet test QuickReplyKit.slnx` — confirm green.
3. Start on the top **next action**. Branch off `main` (`git checkout -b phase-N-name`), implement
   with ponytail, validate, QA with a separate subagent, merge `--no-ff`, then **update this file**.
