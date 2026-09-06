# QuickReplyKit — Project Status (living handoff)

> **This is the single source of truth for "where we are."** Update it at the end of every work
> session. Read [`../CLAUDE.md`](../CLAUDE.md) first for how to build/test/run and the conventions,
> and [`ROADMAP.md`](ROADMAP.md) for the full phase plan (0–7) with per-phase pickup instructions.

- **Last updated:** 2026-09-05
- **Branch layout:** `main` only (always green). Feature work happens on `phase-N-*` branches (built in
  git worktrees so builds don't lock a running app instance).
- **`main` tip:** `8e3d6c9` — Merge phase-6-qa.
- **Health:** `dotnet build` clean (0 warnings) · `dotnet test` **295 passing** · `--selftest-paste` →
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
| 6 | Settings window, window memory + multi-monitor, tray Settings, launch-at-startup | ✅ merged + QA | 295 |
| 7 | Polish, **rich-paste + live-UI E2E**, portable single-file packaging, README, regression | ⏳ in progress | 295 |

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
1. **Phase 7 (in progress)** — rich-paste + live-UI paste self-tests, surface invalid files in the UI
   (BACKLOG), large-library perf test, README, and portable single-file packaging
   (`dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`) verified on a
   clean run. Then a final full-workflow regression. Spec: [`PHASE7-POLISH-PACKAGING.md`](PHASE7-POLISH-PACKAGING.md).
   Packaging + final run-verification are done on `main` (needs the app instance closed to rebuild).

## How to resume in a fresh session
1. Read `CLAUDE.md` then this file, then `ROADMAP.md`.
2. `export PATH="/c/Program Files/dotnet:$PATH"; dotnet build QuickReplyKit.slnx && dotnet test QuickReplyKit.slnx` — confirm green.
3. Start on the top **next action**. Branch off `main`, implement with ponytail (worktree), validate,
   QA with a separate subagent, merge `--no-ff`, then **update this file**.
