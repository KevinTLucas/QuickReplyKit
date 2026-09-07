# QuickReplyKit - Project Status

> Current-state source of truth. Read this after [`../CLAUDE.md`](../CLAUDE.md). Update it with every
> meaningful change; a change is not complete until this file agrees with the code, tests, and backlog.

## Audited Baseline

- **Last audited:** 2026-09-07
- **Current product baseline:** B01-B10 and B12-B13 are merged; use `git rev-parse HEAD` for the current docs-inclusive tip.
- **Build:** clean, 0 warnings / 0 errors (`dotnet build QuickReplyKit.slnx -c Debug`).
- **Tests:** **537 passed** after B12 independent QA on the B13 baseline; build remains clean.
- **Portable publish:** B12's current self-contained build is at `publish/latest/QuickReplyKit.exe`. The canonical
  `publish/QuickReplyKit.exe` was locked by the user's running instance and was deliberately not closed or replaced;
  the next unlocked product publish promotes the latest build. No external .NET install is needed.
- **Windows self-tests:** `--selftest-paste`, `--selftest-paste-rich`, `--selftest-paste-ui`, and
  `--selftest-foreground` all returned exit code 0.
- **Git:** completed feature branches listed below are ancestors of `main`. Unmerged work is isolated in the
  human-readable branches listed under Active worktrees. Several fully merged historical branches/worktrees
  remain locally and may be pruned after confirming no agent still owns them.
- **State:** launchable, but a new user-requested backlog wave is active. [`BACKLOG.md`](BACKLOG.md) contains
  4 ordered items with severity, acceptance criteria, and status. Accessibility is next; sandboxed scripting remains
  deliberately last.

## Merged And QA'd

| Backlog item | Merge | Verification recorded in merge history | State |
|---|---:|---|---|
| B12 discoverable Import/Export | `4e490f4` | 537/537 integrated; focused XAML contract; static keyboard/theme/compact review | Complete |
| B13 configurable backup location | `73090b1`, `7792fb5` | 536/536 integrated; 31/31 focused; invalid, nested, unavailable, migration and restore paths | Complete |
| B10 folder-scoped search chip | `b451f7d` | 524/524 full, 41/41 launcher VM, 10/10 focused; static theme/compact/accessibility review | Complete |
| B09 remove folder tags | `d98f3f0` | 516/516 full, 118/118 focused; response tags and inert legacy sidecars preserved | Complete |
| B08 recoverable folder Trash | `afeabd5` | 519/519; required backup, full subtree restore, conflicts, legacy items, real junction containment | Complete |
| B04 clipboard sanitization | `2b8427a` | 505/505; active/hidden markup, unsafe links, malformed input, offsets and complexity bounds | Complete |
| B06 adaptive Settings/About | `5e49979` | 455/455; owner-monitor/DPI bounds, scrolling fixed actions, wrapped diagnostics | Complete |
| B07 accessible button states | `5b70994` | 455/455; all button families; Light/Dark/High Contrast and arbitrary swatches | Complete |
| B05 search relevance | `80cefa0` | 455/455; 21/21 focused; 4,000-response corpus averaged 7.26 ms/search | Complete |
| B03 import plan integrity | `6aa1ae1` | 452/452; junction race, ZIP limits, malformed/traversal/device paths, stale-plan handling | Complete |
| B02 transactional backup restore | `c6fdda8` | 443/443; rollback/oldest restore 40 repeated runs; all self-tests 3 rounds; both themes | Complete |
| B01 repeated right-click crash | `c596e29` | 436/436; 150 rapid cycles per theme; actions usable; log delta clean | Complete |
| #1 dark-mode Category ComboBox + original #6 right-click fix | `d562052` | 6/6 QA; both themes; original reentrancy path survived 80 rapid clicks | Original fix; later Run crash closed by B01 |
| #7 rolling diagnostics/logging | `8f74aa9` | 9/9 feature QA; rollover, levels, crash channels, no response-body logging | Partial: requested workflow coverage remains |
| #2 editor palettes + #3 visible Edit action | `20d808c` | 6/6 QA; both themes; edit opens pre-filled | Complete |
| #13 About / Diagnostics screen | `23468f7` | Both themes; values, copy, and folder actions checked | Partial: see open work |
| #10 rolling backups + Restore Library | `f303524` | 9/9 feature QA; extraction guard and pre-import backup checked | Partial: restore bug found in audit |
| #8 tag chips + folder sidecars + recursive batch apply | `59547c5` | Chip UX, sidecar exclusion, union/dedupe, direct-copy compatibility checked | Partial: ZIP import + scope choice remain |
| #12 three-card first-run onboarding | `a8a19a8` | Live pager + shown-once persistence; both themes | Complete |
| #11 in-app Trash | `ef5a573` | 7/7 QA; live delete/restore, conflict refusal, purge, path containment | Partial: see open work |
| #4/#5 per-response instant-paste hotkeys + manager | `a0615b2` | 9/9 QA; live no-window paste, clipboard restore, conflict/search checks | Complete |

The final hotkey merge had conflicts in `AppConfig`, `App.xaml.cs`, `SettingsWindow.xaml.cs`, and this status
file. The integrated result was rebuilt and retested during that audit at 435 tests; B01 adds the 436th test.

## What Ships Today

- **Launcher:** compact 3-pane folder/results/preview UI; fast global or folder-scoped search with highlighting, body
  snippets, typo fallback, favorites, recent responses, invalid-file surfacing, keyboard insert, plain insert, copy
  feedback, a visible Edit action, and a labelled Import/Export menu.
- **Paste:** configurable launcher hotkey (default **Ctrl+Alt+D**) plus per-response global hotkeys that paste
  rich or plain without opening the launcher. Focus returns to the prior app; clipboard content is restored;
  transient paste content is marked to stay out of Windows clipboard history.
- **Library/editor:** live-watched folder hierarchy; create/edit/duplicate/rename/move; rich editor with standard
  color/highlight palettes, lists, links, alignment, size, strike, response-tag chips, edit-before-insert, templates,
  safe built-in variables, and response includes. Retired folder-tag sidecars are inert and safely ignored.
- **Safety/sharing:** import preview; zip/file import/export; conflict policies; invalid-file tolerance; importer and
  Trash path-traversal guards; rolling library backups with restore; app-local response and folder Trash with exact
  hierarchy restore.
- **Desktop integration:** System/Light/Dark themes, first-run onboarding, tray, startup registration, persisted
  settings/window placement, About/Diagnostics, rolling logs, large-library background/parallel scan, and
  self-contained single-file publishing.

## Remaining Work

Priority and acceptance detail live in [`BACKLOG.md`](BACKLOG.md). Immediate order:

1. **B11 P1:** full accessibility pass.
2. **B14-B15 P2:** logging and diagnostics completion.
3. **B16 P2:** sandboxed dynamic-variable scripts, only after its accessibility and logging gates.

**Known confirmed regressions/defects:** none on merged `main`.

**Active worktrees:** B01-B10 and B12-B13 are complete. A stale interrupted historical hotkey worktree remains
quarantined and is not an active source of changes.
Each branch's `main...branch` diff is scoped to its named bug; inherited commits in `git log branch` are not branch-owned
work. Branch names must stay human-readable and must not contain AI/vendor names, IDs, or generated session labels.

## Known Limits / Human Checks

- **Current QA boundary:** the user has disabled Computer Use/UIAutomation/screenshot-based validation. Agents must
  use headless tests and static UI review and must not move, focus, or close the user's live app or other windows.
- A highlight with no explicit text color can be low contrast in dark mode, as in Word/Outlook. The editor's
  **Automatic (theme default)** text-color option restores inherited text color.
- `--selftest-paste-rich` proves byte-correct CF_HTML on the clipboard; Win11 RichEdit does not import HTML, so it
  is not an independent rich-render target.
- The independent audit saw `--selftest-paste-ui` fail once and pass immediately on retry. Treat this as a
  focus-sensitive test-harness flake unless a user-facing paste failure reproduces; keep watching it.
- Before broad rollout, manually confirm a real Ctrl+Alt+D paste into Outlook/Teams/browser/CRM and verify the
  transient response does not appear in the Win+V history flyout.
- The portable executable is unsigned, so SmartScreen may prompt. Code signing is a distribution concern, not an
  application defect.

## Cold-Pickup Checklist

1. Read [`../CLAUDE.md`](../CLAUDE.md), this file, then [`../ARCHITECTURE.md`](../ARCHITECTURE.md).
2. Start from `main`; confirm `git status --short --branch` is clean. All old feature branches are merged.
3. Put `C:\Program Files\dotnet` on `PATH`. In an offline sandbox, point `NUGET_PACKAGES` at
   `C:\Users\Kevin_Lucas\.nuget\packages` and restore with `-p:NuGetAudit=false`.
4. Run build + tests; expect **537** green after B12. Registry tests need normal current-user registry access.
5. Take the first item in [`BACKLOG.md`](BACKLOG.md) on a new branch. Use ponytail, then separate-agent QA,
   headless UI/state tests and static XAML review, appropriate non-disruptive self-tests, a no-regression run, and a
   `--no-ff` merge. Do not drive or capture the user's desktop.
6. Update this file, BACKLOG, README, and ARCHITECTURE as appropriate before considering the change done.
