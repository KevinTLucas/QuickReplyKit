# QuickReplyKit - Project Status

> Current-state source of truth. Read this after [`../CLAUDE.md`](../CLAUDE.md). Update it with every
> meaningful change; a change is not complete until this file agrees with the code, tests, and backlog.

## Audited Baseline

- **Last audited:** 2026-09-07
- **Current product feature tip:** `79692a5` (B19 enterprise UI standards polish); use `git rev-parse HEAD` for the
  docs-inclusive `main` tip after the recorded `--no-ff` merge.
- **Current product baseline:** B01-B19 are complete. The active backlog is empty.
- **Build:** clean, 0 warnings / 0 errors (`dotnet build QuickReplyKit.slnx -c Release -p:NuGetAudit=false`).
- **Tests:** **724 passed** after the B19 combined regression and independent UI/accessibility review.
- **Portable publish:** the current `main` release is rebuilt after the B19 merge. Until that publish completes,
  `publish/QuickReplyKit.exe` and `publish/pending/QuickReplyKit.exe` remain the last B16 release. The older
  `publish/latest/QuickReplyKit.exe` is running and locked; do not close it. No external .NET install is needed.
- **Windows self-tests:** `--selftest-paste`, `--selftest-paste-rich`, `--selftest-paste-ui`, and
  `--selftest-foreground` all returned exit code 0.
- **Git:** completed feature branches listed below are ancestors of `main`. Unmerged work is isolated in the
  human-readable branches listed under Active worktrees. Fully merged historical branch labels were pruned; only
  the dirty, interrupted `feature/hotkeys` worktree remains quarantined to avoid discarding unreviewed local state.
- **State:** launchable. [`BACKLOG.md`](BACKLOG.md) has no active items.

## Merged And QA'd

| Backlog item | Merge | Verification recorded in merge history | State |
|---|---:|---|---|
| B19 enterprise UI standards audit | `79692a5` feature tip | 724/724 integrated; independent accessibility review + blocker re-QA; Light/Dark compact/large captures; all four self-tests | Complete |
| B16 sandboxed dynamic-variable scripts | `db07f5e` | 706/706 integrated; 99 foundation tests; separate persistence, evaluator, runtime, import, trust, accessibility, performance, and blocker re-QA | Complete |
| B17 import review selection | `ca730f6` | 572/572 integrated; 41/41 focused; checkbox selection, safe Keep-both conflict default, non-bulk approval seam, immutable plan preserved | Complete |
| B18 selected-folder indication | `57576ed` | 569/569; 45/45 focused; system-color bar plus semibold non-color cue; nav row width preserved | Complete |
| B15 complete About / Diagnostics status | `5c30e8e` | 567/567 integrated; actual hotkey/watcher/logger state; safe path tokens; both themes; separate correctness/privacy/UI QA | Complete |
| B14 complete diagnostics coverage | `7d0ce16` | 563/563 integrated; 19/19 focused; separate correctness/privacy QA; safe stack frames and no export rescan | Complete |
| B11 full accessibility pass | `9763625` | 559/559 integrated; 22/22 focused; separate test and static QA; live-region and 200%-DPI blockers fixed | Complete |
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
| #7 rolling diagnostics/logging | `8f74aa9` | 9/9 feature QA; rollover, levels, crash channels, no response-body logging | Foundation; completed by B14 |
| #2 editor palettes + #3 visible Edit action | `20d808c` | 6/6 QA; both themes; edit opens pre-filled | Complete |
| #13 About / Diagnostics screen | `23468f7` | Initial screen foundation, completed by B15 | Foundation |
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
  feedback, a visible Edit action, and a labelled Import/Export menu. Compact action buttons wrap without clipping;
  pointer states are restrained while keyboard focus remains explicit.
- **Paste:** configurable launcher hotkey (default **Ctrl+Alt+D**) plus per-response global hotkeys that paste
  rich or plain without opening the launcher. Focus returns to the prior app; clipboard content is restored;
  transient paste content is marked to stay out of Windows clipboard history.
- **Library/editor:** live-watched folder hierarchy; create/edit/duplicate/rename/move; rich editor with standard
  color/highlight palettes, lists, links, alignment, size, strike, response-tag chips, edit-before-insert, templates,
  safe built-in variables, response includes, and bounded trusted `{{script:Name}}` expressions. The Script Manager
  supports create/edit/validate/trust/enable/export/delete and protects dirty drafts.
- **Safety/sharing:** selectable import preview; zip/file import/export; per-conflict policies; invalid-file tolerance;
  exact per-script source review with no bulk code acceptance; local source-bound trust; importer and Trash
  path-traversal guards; rolling library backups with restore; app-local response and folder Trash with exact hierarchy
  restore.
- **Desktop integration:** System/Light/Dark themes, first-run onboarding, tray, startup registration, persisted
  settings/window placement, About/Diagnostics, rolling logs, large-library background/parallel scan, and
  self-contained single-file publishing. Editor toggles expose active and mixed selection states; collection dialogs
  resize cleanly, clipped values retain full tooltips, and High Contrast states use paired system colors.

## Remaining Work

No active implementation remains. Add future requested work to [`BACKLOG.md`](BACKLOG.md) before starting it.

**Known confirmed regressions/defects:** none on merged `main`.

**Active worktrees:** none after the B19 integration worktree is removed. A stale interrupted historical hotkey
worktree remains quarantined and is not an active source of changes.
Each branch's `main...branch` diff is scoped to its named bug; inherited commits in `git log branch` are not branch-owned
work. Branch names must stay human-readable and must not contain AI/vendor names, IDs, or generated session labels.

## Known Limits / Human Checks

- **Current QA boundary:** app launches, project screenshot/capture scripts, and interactive view inspection are allowed
  again. Do not use GPT Computer Use/UIAutomation or touch unrelated user windows. Close every test-created app window
  normally; reuse one Notepad tab if paste verification genuinely needs it.
- A highlight with no explicit text color can be low contrast in dark mode, as in Word/Outlook. The editor's
  **Automatic (theme default)** text-color option restores inherited text color.
- `--selftest-paste-rich` proves byte-correct CF_HTML on the clipboard; Win11 RichEdit does not import HTML, so it
  is not an independent rich-render target.
- The independent audit saw `--selftest-paste-ui` fail once and pass immediately on retry. Treat this as a
  focus-sensitive test-harness flake unless a user-facing paste failure reproduces; keep watching it.
- Before broad rollout, manually confirm a real Ctrl+Alt+D paste into Outlook/Teams/browser/CRM and verify the
  transient response does not appear in the Win+V history flyout.
- Script trust protects against unreviewed portable definitions, accidental source changes, malformed files, and
  concurrent app workflows. A malicious process already running as the same Windows user can modify the local trust
  ledger and is outside this application-level security boundary.
- The portable executable is unsigned, so SmartScreen may prompt. Code signing is a distribution concern, not an
  application defect.

## Cold-Pickup Checklist

1. Read [`../CLAUDE.md`](../CLAUDE.md), this file, then [`../ARCHITECTURE.md`](../ARCHITECTURE.md).
2. Start from `main`; confirm `git status --short --branch` is clean. All old feature branches are merged.
3. Put `C:\Program Files\dotnet` on `PATH`. In an offline sandbox, point `NUGET_PACKAGES` at
   `C:\Users\Kevin_Lucas\.nuget\packages` and restore with `-p:NuGetAudit=false`.
4. Run build + tests; expect **724** green after B19. Registry tests need normal current-user registry access.
5. If [`BACKLOG.md`](BACKLOG.md) gains a requested item, take its first active entry on a new branch. Use ponytail, then separate-agent QA,
   headless UI/state tests and static XAML review, appropriate non-disruptive self-tests, a no-regression run, and a
   `--no-ff` merge. UI work also requires app launch, interaction-state inspection, and representative screenshots.
6. Update this file, BACKLOG, README, and ARCHITECTURE as appropriate before considering the change done.
