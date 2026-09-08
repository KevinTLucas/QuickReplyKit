# QuickReplyKit - Project Status

> Current-state source of truth. Read this after [`../README.md`](../README.md). Update it with every
> meaningful change; a change is not complete until this file agrees with the code, tests, and backlog.

## Audited Baseline

- **Last audited:** 2026-09-07
- **Current product merge:** `99d8efd` (B28 icon-only Import / Export trigger);
  use `git rev-parse HEAD` for the docs-inclusive tip.
- **Current product baseline:** B01-B20 and B22-B28 are complete. B21 passed independent QA and awaits current-main
  regression; B29 passed branch QA and awaits current-main integration; B30 remains queued last.
- **Build:** clean, 0 warnings / 0 errors (`dotnet build QuickReplyKit.slnx -c Release -p:NuGetAudit=false`).
- **Tests:** **732 passed** on merged `main` after B28.
- **Portable publish:** `publish/QuickReplyKit.exe` was rebuilt from `99d8efd` at 69,920,438 bytes (SHA-256
  `9F1269BF074E76A1851BAC326F6AA09F888214FCCAA2454687452F57E009B319`). The prior packaged rich-clipboard gate exits 0.
  The GUI-hosted paste gate returned 1 when this automation host could not complete its foreground step; its two
  temporary Notepad processes were removed and the user's existing Notepad was not touched. Repeat all four packaged
  gates after the remaining backlog integrations. No external .NET install is needed.
- **Windows self-tests:** `--selftest-paste`, `--selftest-paste-rich`, `--selftest-paste-ui`, and
  `--selftest-foreground` all returned exit code 0 for three consecutive branch rounds and one final packaged
  `b5a6ff0` round; every temporary Notepad window closed normally and no visible Notepad remained.
- **Git:** `origin` is `https://github.com/KevinTLucas/QuickReplyKit.git`; its initial MIT-license commit was
  preserved when the local history was connected. Completed feature branches listed below are ancestors of `main`.
  Root `AGENTS.md` and `CLAUDE.md` files are local-only and ignored by Git. Unmerged work is isolated in the
  human-readable branches listed under Active worktrees. Fully merged historical branch labels were pruned; only
  the dirty, interrupted `feature/hotkeys` worktree remains quarantined to avoid discarding unreviewed local state.
- **State:** launchable. The confirmed import crash is fixed and independently regression-tested. B21 native-script
  implementation is isolated and independently QA'd but not yet shipped. See [`BACKLOG.md`](BACKLOG.md) for B21 and
  B29-B30.

## Merged And QA'd

| Backlog item | Merge | Verification recorded in merge history | State |
|---|---:|---|---|
| B28 icon-only Import / Export trigger | `99d8efd` | 732/732; 25/25 focused; Light/Dark/compact captures; keyboard menu anchored to trigger | Complete |
| B27 native selected-folder background | `44f9267` | 732/732; 47/47 focused; Light/Dark/compact captures; High Contrast system brushes and non-color cue verified | Complete |
| B26 short scoped-search label | `1ffac1f` | 732/732; 47/47 focused; Light/Dark/compact captures; duplicate leaves retain full-path filtering and accessibility | Complete |
| B25 backup and Trash settings layout | `8658443` | 730/730; 28/28 focused; Light/Dark/minimum-height captures; scrolling content and fixed actions verified | Complete |
| B24 remove shortcut-combination lookup | `d1f39a0` | 729/729; 94/94 focused; Light/Dark/minimum-size captures; normal filter/conflict/Edit/Clear preserved | Complete |
| B23 About / Diagnostics chrome cleanup | `2e23918` | 731/731; 30/30 focused; Light/Dark/minimum-size captures; native X, Alt+F4 and Escape verified | Complete |
| B22 per-user paths and truthful diagnostics | `dc4b2a8` | 730/730; exact resolved paths; saved custom paths; no hotkey values/content; invalid and environment-variable paths | Complete |
| B20 import workflow crash | `ba621f5` | 729/729; exact XAML root fixed; full UI boundary containment; transactional script trust/write rollback; independent re-QA | Complete |
| Notepad self-test reliability/cleanup | `b5a6ff0` | 724/724; all four self-tests x3; foreground-denied fallback; exact temp window closes normally | Complete |
| B19 enterprise UI standards audit | `1bf0d66` | 724/724 integrated; independent accessibility review + blocker re-QA; Light/Dark compact/large captures; all four self-tests | Complete |
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
| #10 rolling backups + Restore Library | `f303524` | 9/9 feature QA; extraction guard and pre-import backup checked | Foundation; completed by B02/B13 |
| #8 tag chips + folder sidecars + recursive batch apply | `59547c5` | Chip UX, sidecar exclusion, union/dedupe, direct-copy compatibility checked | Superseded: folder tags removed by B09; response tags ship |
| #12 three-card first-run onboarding | `a8a19a8` | Live pager + shown-once persistence; both themes | Complete |
| #11 in-app Trash | `ef5a573` | 7/7 QA; live delete/restore, conflict refusal, purge, path containment | Foundation; completed by B08 |
| #4/#5 per-response instant-paste hotkeys + manager | `a0615b2` | 9/9 QA; live no-window paste, clipboard restore, conflict/search checks | Complete |

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

B21 and B29-B30 are active in [`BACKLOG.md`](BACKLOG.md). B21 passed independent security/UI QA and awaits current-main
regression; B29 passed independent wording/interaction QA and awaits current-main integration; B30 is explicitly last.
Each implementation requires a scoped human-readable branch, independent QA, green full
regression, documentation updates, `--no-ff` merge, and a portable rebuild from the resulting `main` tip.

**B21 branch evidence:** `feature/native-script-runtime` builds Release with 0 warnings / 0 errors and passes
757/757 tests after integration with current `main`. Independent QA found and fixed three root causes: redirected
PowerShell stdin now uses UTF-8, rapid instant-hotkey presses cannot overlap, and the Script Manager uses the shared
dark-safe ComboBox plus a compact-height scroll path. Runtime/security tests cover source/output/time/concurrency
bounds, Unicode, generic failures, privacy-safe diagnostics, cancellation, process-tree cleanup, exact source-bound
trust, transactional import, stale/rollback refusal, and old `qrk-expr-1` compatibility. Script Manager and Import
Review were visually inspected in Light, Dark, and minimum-size captures; temporary capture hooks were removed before
the final Release rebuild. It is ready to merge but not shipped yet.

**Known confirmed regressions/defects:** none after B20.

**Active worktrees:** isolated branches for B21 and B29 are listed by `git worktree list`; neither is merged until its
individual QA and integrated regression pass. A stale interrupted historical hotkey worktree remains quarantined and
is not an active source of changes.
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
- The Notepad-driven self-tests use real Ctrl+V when Windows grants the test process foreground access. When an
  automation host is correctly denied foreground input, they use a test-only `WM_PASTE` trigger while retaining the
  real clipboard snapshot/write/restore and launcher-command paths. Each run opens one empty temporary document,
  undoes the paste, and closes that exact window normally.
- Before broad rollout, manually confirm a real Ctrl+Alt+D paste into Outlook/Teams/browser/CRM and verify the
  transient response does not appear in the Win+V history flyout.
- Script trust protects against unreviewed portable definitions, accidental source changes, malformed files, and
  concurrent app workflows. A malicious process already running as the same Windows user can modify the local trust
  ledger and is outside this application-level security boundary.
- The portable executable is unsigned, so SmartScreen may prompt. Code signing is a distribution concern, not an
  application defect.

## Cold-Pickup Checklist

1. Read [`../README.md`](../README.md), this file, then [`../ARCHITECTURE.md`](../ARCHITECTURE.md).
2. Start from `main`; confirm `git status --short --branch` is clean. All old feature branches are merged.
3. Put `C:\Program Files\dotnet` on `PATH`. In an offline sandbox, point `NUGET_PACKAGES` at
   `C:\Users\Kevin_Lucas\.nuget\packages` and restore with `-p:NuGetAudit=false`.
4. Run build + tests; expect **732** green after B27. Registry tests need normal current-user registry access.
5. If [`BACKLOG.md`](BACKLOG.md) gains a requested item, take its first active entry on a new branch. Use ponytail, then separate-agent QA,
   headless UI/state tests and static XAML review, appropriate non-disruptive self-tests, a no-regression run, and a
   `--no-ff` merge. UI work also requires app launch, interaction-state inspection, and representative screenshots.
6. Update this file, BACKLOG, README, and ARCHITECTURE as appropriate before considering the change done.
