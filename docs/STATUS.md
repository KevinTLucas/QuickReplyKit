# QuickReplyKit — Project Status (living handoff)

> **This is the single source of truth for "where we are."** Update it at the end of every work
> session. Read [`../CLAUDE.md`](../CLAUDE.md) first for how to build/test/run and the conventions,
> and [`ROADMAP.md`](ROADMAP.md) for the full phase plan (0–7).

- **Last updated:** 2026-09-06
- **Status: ✅ MVP + all V2 features complete** (see [`V2-PLAN.md`](V2-PLAN.md)). Merged to `main`:
  **Phase R** (lenient parsing + watcher guard), **Phase 8** (full-UI light/dark theming), **Phase 9**
  (template engine: allow-listed `{{greeting}}/{{date}}/{{time}}/{{day}}/{{date:fmt}}` + response
  includes `{{name}}` with cycle detection + a fan-out expansion budget — resolved at insert/preview/copy),
  **Phase 10** (richer editor: size/heading, text color, highlight, alignment, strikethrough; + edit-before-insert
  compose mode). All independently QA'd; one real defect fixed (include fan-out DoS).
- **Now: Phase 11+ continuous improvement** — end-user-driven polish pass (see `V2-PLAN.md`).
- **`main` tip:** `21f2c67`. **`dotnet test` 384 passing**, build clean, all four self-tests ok.
- **Known minor:** (1) editable rename/category `ComboBox` closed field keeps native light chrome in dark
  mode (text readable); (2) a highlight color with no explicit text color can be low-contrast in dark mode
  (same as Word/Outlook). Both documented, non-blocking.
- **(historical) MVP:** All phases 0–7 implemented, independently QA'd, and merged to `main`.
- **Health:** `dotnet build` clean (0 warnings) · `dotnet test` **384 passing** · all four E2E self-tests
  deterministic: `--selftest-paste` → `PASTE=ok CLIPBOARD_RESTORED=ok`, `--selftest-paste-rich` →
  `RICHPASTE=ok`, `--selftest-paste-ui` → `UIPASTE=ok`, `--selftest-foreground` → `FOREGROUND_TRACK=ok`.
- **Portable exe: verified.** `dotnet publish` produces a self-contained single-file `QuickReplyKit.exe`
  (~67 MB) + `DefaultLibrary`; runs from a clean path, seeds the library, registers the hotkey, renders.
- **Run:** `dotnet run --project src/QuickReplyKit -- --show` (or the portable exe). Global hotkey: **Ctrl+Alt+D**.

## Awaiting user confirmation (2 human spot-checks — not engineering blockers)
These are inherently un-automatable; everything they exercise is covered by self-tests at the mechanism level:
1. **Live paste through the real UI**: focus a third-party app → **Ctrl+Alt+D** → pick a response →
   Enter/Paste → text lands at the caret, focus returns, clipboard intact. (Auto-covered by `--selftest-paste-ui`.)
2. **Clipboard-history absence**: confirm pasted content does NOT appear in the Win+V flyout.
   (Auto-covered at the API level — the three exclusion formats are set and unit-verified.)

## Phase progress — all done
| Phase | Scope | Status | Tests |
|---|---|---|---|
| 0 | Skeleton: single-instance, tray, icon, DPI manifest, `--show`, screenshot tooling | ✅ merged | — |
| 1 | Core: model, scanner, live watcher, search, HTML→text, CF_HTML, settings/recent, seeding | ✅ merged + QA | 99 |
| 2 | Launcher UI: 3-pane nav/results/preview, live search, keyboard nav, favorites, action bar | ✅ merged + QA | 142 |
| 3 | Paste engine: hotkey, focus restore, clipboard-safe insert, history-exclusion, `ForegroundMonitor` | ✅ merged + QA | 224 |
| 4 | Rich-text editor + response management (create/edit/duplicate/rename/move/delete, ctx menu) | ✅ merged (Core QA'd) | 236 |
| 5 | Import/Export (Core + UI, conflict/summary; **zip path-traversal fixed**) | ✅ merged + QA | 266 |
| 6 | Settings window, window memory + multi-monitor, tray Settings, launch-at-startup | ✅ merged + QA | 295 |
| 7 | Polish, rich + live-UI paste E2E, invalid-file surfacing, perf test, README, portable packaging | ✅ merged + QA | 297 |

## Known gaps / notes (non-blocking)
- **Phase 4 UI** never had a *dedicated* independent QA pass (its Core did; the final Phase-7 QA swept
  the whole app and found no defects). Fine to leave; revisit if issues surface.
- `--selftest-paste-rich` proves the clipboard carries a well-formed **CF_HTML** payload (what Outlook/
  Word/browsers/Teams consume as rich); it does not render in a test control because Win11's Msftedit
  RichEdit has no HTML importer. Real rich targets render it.
- Deferred robustness items + the **V2 feature list** (richer editor, edit-before-insert, dynamic code
  variables, response includes) are in [`BACKLOG.md`](BACKLOG.md). **V2 is not started** — do not begin
  until the user asks.
- Distribution folder includes `.pdb` files; add `<DebugType>embedded</DebugType>` under a Release
  condition if a leaner folder is wanted.

## How to resume / extend in a fresh session
1. Read `CLAUDE.md`, then this file, then `ROADMAP.md`.
2. `export PATH="/c/Program Files/dotnet:$PATH"; dotnet build QuickReplyKit.slnx && dotnet test QuickReplyKit.slnx` — confirm 297 green.
3. New work = a branch off `main` (worktree), ponytail, validate, independent-QA subagent, merge `--no-ff`, update this file.
4. For V2, see `BACKLOG.md` (only when the user requests it).
