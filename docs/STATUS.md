# QuickReplyKit — Project Status (living handoff)

> **This is the single source of truth for "where we are."** Update it at the end of every work
> session. Read [`../CLAUDE.md`](../CLAUDE.md) first for how to build/test/run and the conventions.

- **Last updated:** 2026-09-05
- **Branch layout:** `main` only (always green). Feature work happens on `phase-N-*` branches.
- **`main` tip:** `0f8fead` — Merge phase-4-ui.
- **Health:** `dotnet build` clean (0 warnings) · `dotnet test` **236 passing** · paste self-test
  `PASTE=ok CLIPBOARD_RESTORED=ok` (engine-level; see the bug below for the real-app gap).
- **Launchable?** Yes: `dotnet run --project src/QuickReplyKit -- --show` (also runs to the tray).

---

## 🔴 TOP PRIORITY — paste into the active app "does nothing" (user-reported)
**Status: OPEN. Fix this before any new feature work.**

**Repro (user):** Open Notepad. Separately launch QuickReplyKit (fresh launch, e.g. `--show`/run).
Pick a response, click **Paste**. The launcher closes but nothing appears in Notepad.

**Diagnosis (to verify):** The paste **target window** is captured in `App.CaptureForegroundTarget()`
which runs inside `ShowLauncher()` and reads `GetForegroundWindow()` at that instant. When the app is
**launched fresh** (from a terminal/IDE or via `--show`), the foreground window at show-time is the
launching process — **not Notepad** — so `ForegroundTargetTracker.Target` is the wrong window (or the
launcher). Clicking Paste then restores focus to the wrong window and sends Ctrl+V there → nothing in
Notepad. The engine mechanism itself is proven correct by `--selftest-paste` (which sets the target
explicitly) and by `ClipboardWriterTests` (history-exclusion formats verified).

**So:** the intended **hotkey** flow (Notepad focused → press the global hotkey → target = Notepad →
Enter/Paste) likely already works, because the hotkey captures foreground *before* we steal focus.
The bug bites the tray/`--show`/fresh-launch paths, and possibly any path where the last real app
isn't foreground at show-time.

**Proposed fix (do this next):**
1. **Continuously track the last real foreground window** instead of only sampling at show-time.
   Add a `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, …)` in the app that records the most recent
   foreground HWND that is **not** a QuickReplyKit window; use that as the paste target. This makes
   tray, hotkey, and fresh-launch all reliable.
2. Keep the hotkey-time capture as a belt-and-suspenders.
3. If the resolved target is 0 / our own window, keep the existing graceful fallback (copy to
   clipboard only) but surface a subtle hint.
4. **Verify for real**, not just via `--selftest-paste`: drive the actual launcher (open over Notepad
   via the hotkey, arrow to a response, press Enter and click Paste) and confirm insertion + focus
   return + clipboard restore. Consider a `--selftest-paste-ui` that exercises the live window path.
5. Tell the user the correct usage: focus your target app → press the **global hotkey**
   (`Ctrl+Alt+Space` default) → pick → Enter. (Also make fresh-launch work per #1.)

Files in play: `src/QuickReplyKit/App.xaml.cs` (`CaptureForegroundTarget`, wiring),
`src/QuickReplyKit/Interop/ForegroundWindow.cs`, `Interop/ForegroundTargetTracker.cs`,
`Services/InsertService.cs`, `ViewModels/LauncherViewModel.cs` (mode/hide ordering).

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
- 🔴 The paste target bug above (top priority).
- **Rich-paste E2E is unproven** — self-test only pastes *plain* into Notepad. Add a rich (CF_HTML)
  paste check (WordPad is gone on Win11 24H2; use Word if present, a Win32 RichEdit harness, or an
  in-process RichTextBox) — see BACKLOG. Do this in Phase 7 final verification.
- History-exclusion is proven at the API level (formats set) but **not** confirmed absent from the
  real Win+V UI / Cloud Clipboard — needs a human spot-check.
- Deferred robustness items: [`BACKLOG.md`](BACKLOG.md) (root-level invalid files not surfaced;
  strict per-field JSON parsing; empty-folder-in-empty-library message).

## Immediate next actions (in order)
1. **Fix the paste target bug** (see top section). Highest priority.
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
