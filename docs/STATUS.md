# QuickReplyKit - Project Status

> Current-state source of truth. Maintainers read this after [`DEVELOPMENT.md`](DEVELOPMENT.md); the root
> [`README.md`](../README.md) is the end-user guide. Update this file with every
> meaningful change; a change is not complete until this file agrees with the code, tests, and backlog.

## Audited Baseline

- **Last audited:** 2026-09-09
- **Current product merge:** `713a906`; B01-B39 are complete and merged on `main`.
- **Current product baseline:** Microsoft Word formatting compatibility is accepted. QuickReplyKit explicitly requests
  Word's Keep Source Formatting behavior for rich Paste, preserves Word/RTF block-inherited formatting through editor
  Save, and prefers RTF for New from Clipboard.
- **Build:** clean, 0 warnings / 0 errors (`dotnet build QuickReplyKit.slnx -c Release -p:NuGetAudit=false`).
- **Tests:** **806 passed** on merged `main`.
- **Portable publish:** `release/QuickReplyKit.exe` reflects `713a906` (69,929,560 bytes; SHA-256
  `309C9402B0F99BD3D29946EF006EDE34796DD02FC30E15E47DC9DB1150BA6CA7`). The portable build is self-contained; no
  external .NET or Visual C++ installation is needed.
- **Windows self-tests:** all four gates pass against both the integrated Release build and canonical packaged release:
  `PASTE=ok CLIPBOARD_RESTORED=ok`, `RICHPASTE=ok`, `UIPASTE=ok`, and `FOREGROUND_TRACK=ok`.
- **Git:** `origin` is `https://github.com/KevinTLucas/QuickReplyKit.git`; its initial MIT-license commit was
  preserved when the local history was connected. Completed feature branches listed below are ancestors of `main`.
  Root `AGENTS.md` and `CLAUDE.md` files are local-only and ignored by Git. Unmerged work is isolated in the
  human-readable branches listed under Active worktrees. Fully merged historical branch labels were pruned; only
  the dirty, interrupted `feature/hotkeys` worktree remains quarantined to avoid discarding unreviewed local state.
- **State:** feature-complete, launchable, green, independently QA'd, and accepted in real Microsoft Word. No active
  backlog work remains. The completed Word feature/QA branches are deleted after this release is pushed.

## Merged And QA'd

| Backlog item | Merge | Verification recorded in merge history | State |
|---|---:|---|---|
| B35 Microsoft Word rich-format fidelity | `713a906` | 806/806; every exposed format/palette through JSON + RTF; four packaged self-tests; independent re-QA; real-Word acceptance passed | Complete |
| B39 deterministic self-test target/cleanup | `41ab682` | 793/793; independent ownership/failure-path QA; all four gates x8 total; Notepad PID set unchanged; zero child-process leaks | Complete |
| B38 remove full-trust PowerShell scripts | `31179f7` | 791/791; independent security/correctness/ponytail review; native runtime, async plumbing, UI, examples and docs removed; restricted expressions and exact-source import review retained | Complete |
| B36 rich editor formatting persistence | `0ae7786` | 811/811; 57/57 focused; two independent exact-tip reviews; every exposed editor format, apply/remove, JSON reload, mixed selections, list/link edge cases | Complete |
| B34 parser-backed Word RTF conversion | `b1749ff` | 788/788; RTF round-trip covers bold, italic, color and font size | Complete |
| B32 end-user GitHub guide | `1f9839b` | 785/785; 11 privacy-checked images; links/HTML/Markdown/labels/paths/security wording independently verified | Complete |
| B31 whole-app UI consistency | `08de27d` | 785/785; 20/20 focused; Light/Dark/compact captures; real automation peers, live-DPI sizing, themed menus, keyboard and layout checks | Complete |
| B30 restricted dynamic-variable example | `fc3a5a5` | Restricted expression and response example retained; unrestricted examples removed by B38 | Complete |
| B29 response edit label | `8a3de68` | 761/761; 4/4 focused; Light/Dark/compact context-menu and compose captures; handler and transient behavior unchanged | Complete |
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
  feedback, a visible Edit action, and an icon-only Import/Export menu with explicit tooltip and automation name.
  Compact action buttons wrap without clipping;
  pointer states are restrained while keyboard focus remains explicit.
- **Paste:** configurable launcher hotkey (default **Ctrl+Alt+D**) plus per-response global hotkeys that paste
  rich or plain without opening the launcher. QuickReplyKit attempts to restore the prior app and common clipboard
  formats; transient paste content is marked so Windows should not retain it in clipboard history. These behaviors
  remain best-effort across privilege, application, and clipboard-format boundaries.
- **Library/editor:** live-watched folder hierarchy; create/edit/duplicate/rename/move; rich editor with standard
  color/highlight palettes, lists, links, alignment, size, strike, response-tag chips, edit-before-insert, templates,
  safe built-in variables, response includes, and restricted `{{script:Name}}` expressions. Script failures stop
  before paste/copy and show a dialog or tray notice. The Script Manager supports
  create/edit/validate/trust/enable/export/delete and protects dirty drafts.
- **Safety/sharing:** selectable import preview; zip/file import/export; per-conflict policies; invalid-file tolerance;
  exact per-script source/language review with no bulk code acceptance; local source-bound trust; importer and Trash
  path-traversal guards; rolling library backups with restore; app-local response and folder Trash with exact hierarchy
  restore.
- **Desktop integration:** System/Light/Dark themes, first-run onboarding, tray, startup registration, persisted
  settings/window placement, About/Diagnostics, rolling logs, large-library background/parallel scan, and
  self-contained single-file publishing. Editor toggles expose active and mixed selection states; collection dialogs
  resize cleanly, clipped values retain full tooltips, and High Contrast states use paired system colors.

## Remaining Work

No active backlog items or known confirmed regressions remain. `feature/hotkeys` is a stale local-only interrupted
worktree retained solely to avoid discarding unreviewed filesystem state; it is not active and must not be merged.
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
- Target-based self-tests use real Ctrl+V when Windows grants the test process foreground access. When an automation
  host is correctly denied foreground input, they use a test-only `WM_PASTE` trigger while retaining the real
  clipboard snapshot/write/restore and launcher-command paths. Each run owns and closes one exact child process.
- Before broad rollout, manually confirm a real Ctrl+Alt+D paste into Outlook/Teams/browser/CRM and verify the
  transient response does not appear in the Win+V history flyout.
- Script trust protects against unreviewed portable definitions, accidental source changes, and malformed files.
- The portable executable is unsigned, so SmartScreen may prompt. Code signing is a distribution concern, not an
  application defect.

## Cold-Pickup Checklist

1. Read [`DEVELOPMENT.md`](DEVELOPMENT.md), this file, then [`../ARCHITECTURE.md`](../ARCHITECTURE.md). The root
   [`README.md`](../README.md) is the end-user guide.
2. Start from `main`; confirm `git status --short --branch` is clean. All old feature branches are merged.
3. Put `C:\Program Files\dotnet` on `PATH`. In an offline sandbox, point `NUGET_PACKAGES` at
   `$env:USERPROFILE\.nuget\packages` and restore with `-p:NuGetAudit=false`.
4. Run build + tests; expect **806** green on `main`. Registry tests need normal
   current-user registry access.
5. If [`BACKLOG.md`](BACKLOG.md) gains a requested item, take its first active entry on a new branch. Use ponytail, then separate-agent QA,
   headless UI/state tests and static XAML review, appropriate non-disruptive self-tests, a no-regression run, and a
   `--no-ff` merge. UI work also requires app launch, interaction-state inspection, and representative screenshots.
6. Update this file, BACKLOG, README, and ARCHITECTURE as appropriate before considering the change done.
