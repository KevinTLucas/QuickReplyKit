# QuickReplyKit — Project Status (living handoff)

> **This is the single source of truth for "where we are."** Update it at the end of every work
> session. Read [`../CLAUDE.md`](../CLAUDE.md) first for how to build/test/run and the conventions,
> and [`ROADMAP.md`](ROADMAP.md) for the full phase plan (0–7) with per-phase pickup instructions.

- **Last updated:** 2026-09-05
- **Branch layout:** `main` only (always green). Feature work happens on `phase-N-*` branches (built in
  git worktrees so builds don't lock a running app instance).
- **`main` tip:** `61020bb` — Merge phase-5-qa.
- **Health:** `dotnet build` clean (0 warnings) · `dotnet test` **266 passing** · `--selftest-paste` →
  `PASTE=ok CLIPBOARD_RESTORED=ok` · `--selftest-foreground` → `FOREGROUND_TRACK=ok`.
- **Launchable?** Yes: `dotnet run --project src/QuickReplyKit -- --show` (also runs to the tray).
  Global hotkey: **Ctrl+Alt+D**.

## Active work
Autonomous `/loop` in progress (dynamic self-paced): implementing remaining phases via worktree
subagents, each validated by a separate QA subagent, merged `--no-ff` when green. **Next up: Phase 6 UI.**

## Awaiting user confirmation
- **Paste fix** (`ForegroundMonitor`, merged) — the automated `--selftest-foreground`/`--selftest-paste`
  pass, but the live click-through-the-UI paste needs a real hotkey test: focus an app → **Ctrl+Alt+D**
  → pick → Enter/Paste → text lands at the caret. (User was verifying this.)

## Phase progress
| Phase | Scope | Status | Tests |
|---|---|---|---|
| 0 | Skeleton: single-instance, tray, icon, DPI manifest, `--show`, screenshot tooling | ✅ merged | — |
| 1 | Core: model, scanner, live watcher, search, HTML→text, CF_HTML, settings/recent, seeding | ✅ merged + QA | 99 |
| 2 | Launcher UI: 3-pane nav/results/preview, live search, keyboard nav, favorites, action bar | ✅ merged + QA | 142 |
| 3 | **Paste engine**: hotkey, focus restore, clipboard-safe insert, history-exclusion (+`ForegroundMonitor` target fix) | ✅ merged + QA | 224 |
| 4 | Rich-text editor + response management (create/edit/duplicate/rename/move/delete, ctx menu) | ✅ merged (Core QA'd; UI QA pending) | 236 |
| 5 | Import/Export (Core `LibraryPorter` + UI menus/conflict/summary; **zip path-traversal fixed**) | ✅ merged + QA | 266 |
| 6 | Settings window, window memory + multi-monitor, tray Settings | ⏳ Core ✅ (`WindowPlacement`) / **UI next** | 266 |
| 7 | Polish, **rich-paste E2E**, portable single-file packaging, README | ☐ not started | — |

## Needs QA / known gaps
- **Phase 4 UI** never had a dedicated independent QA pass (Core did) — worth a sweep in Phase 7.
- **Rich-paste E2E unproven** — self-test pastes only *plain* into Notepad; add a rich (CF_HTML) check
  in Phase 7 (WordPad gone on Win11 24H2 — use Word/COM, a Win32 RichEdit harness, or in-proc RichTextBox).
- **Live-UI paste regression** — add `--selftest-paste-ui` that drives the real launcher window (Phase 7).
- History-exclusion proven at API level (formats set); real Win+V-absence needs a human spot-check.
- `ImportCoordinator.CountConflicts` isn't path-traversal-guarded (cosmetic only — write side is guarded;
  QA left it deliberately).
- Deferred robustness items: [`BACKLOG.md`](BACKLOG.md).

## Immediate next actions (in order)
1. **Phase 6 UI** — Settings window (hotkey capture → `GlobalHotkey.Rebind`; library path; default paste
   mode; launch-at-startup), window size/position memory using `WindowPlacement.EnsureVisible` (feed it
   `Screen.AllScreens` work areas on show), tray **Settings** wiring. Branch `phase-6-ui`. Spec:
   [`PHASE6-SETTINGS-WINDOW.md`](PHASE6-SETTINGS-WINDOW.md).
2. **Phase 7** — polish, rich-paste + live-UI E2E, `dotnet publish` self-contained single-file portable
   exe, README + user docs, full-workflow regression, large-library perf. Spec:
   [`PHASE7-POLISH-PACKAGING.md`](PHASE7-POLISH-PACKAGING.md).

## How to resume in a fresh session
1. Read `CLAUDE.md` then this file, then `ROADMAP.md`.
2. `export PATH="/c/Program Files/dotnet:$PATH"; dotnet build QuickReplyKit.slnx && dotnet test QuickReplyKit.slnx` — confirm green.
3. Start on the top **next action**. Branch off `main`, implement with ponytail (worktree), validate,
   QA with a separate subagent, merge `--no-ff`, then **update this file**.
