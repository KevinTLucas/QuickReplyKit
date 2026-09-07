# QuickReplyKit - Project Status

> Current-state source of truth. Read this after [`../CLAUDE.md`](../CLAUDE.md). Update it with every
> meaningful change; a change is not complete until this file agrees with the code, tests, and backlog.

## Audited Baseline

- **Last audited:** 2026-09-07
- **Product-code baseline:** `main` at `a0615b2` (this handoff-audit branch changes documentation only).
- **Build:** clean, 0 warnings / 0 errors (`dotnet build QuickReplyKit.slnx -c Debug`).
- **Tests:** **435 passed, 0 failed, 0 skipped**.
- **Windows self-tests:** `--selftest-paste`, `--selftest-paste-rich`, `--selftest-paste-ui`, and
  `--selftest-foreground` all returned exit code 0.
- **Git:** every completed feature branch listed below is already an ancestor of `main`; there are no
  unmerged feature commits. Several merged branches/worktrees remain locally and may be pruned later.
- **State:** launchable and substantially feature-complete. Thirteen requested implementation items were
  merged after independent QA, but seven original backlog items still have gaps in [`BACKLOG.md`](BACKLOG.md). Do not call
  the entire backlog complete until those gaps are closed and independently validated.

## Merged And QA'd

| Backlog item | Merge | Verification recorded in merge history | State |
|---|---:|---|---|
| #1 dark-mode Category ComboBox + #6 repeated-right-click crash | `d562052` | 6/6 QA; both themes; 80 rapid right-clicks | Complete |
| #7 rolling diagnostics/logging | `8f74aa9` | 9/9 feature QA; rollover, levels, crash channels, no response-body logging | Partial: requested workflow coverage remains |
| #2 editor palettes + #3 visible Edit action | `20d808c` | 6/6 QA; both themes; edit opens pre-filled | Complete |
| #13 About / Diagnostics screen | `23468f7` | Both themes; values, copy, and folder actions checked | Partial: see open work |
| #10 rolling backups + Restore Library | `f303524` | 9/9 feature QA; extraction guard and pre-import backup checked | Partial: restore bug found in audit |
| #8 tag chips + folder sidecars + recursive batch apply | `59547c5` | Chip UX, sidecar exclusion, union/dedupe, direct-copy compatibility checked | Partial: ZIP import + scope choice remain |
| #9 import preview | `7aedb29` | Both themes; read-only preview; New/Changed/Identical classification | Partial: import-integrity bugs found in audit |
| #12 three-card first-run onboarding | `a8a19a8` | Live pager + shown-once persistence; both themes | Complete |
| #11 in-app Trash | `ef5a573` | 7/7 QA; live delete/restore, conflict refusal, purge, path containment | Partial: see open work |
| #4/#5 per-response instant-paste hotkeys + manager | `a0615b2` | 9/9 QA; live no-window paste, clipboard restore, conflict/search checks | Complete |

The final hotkey merge had conflicts in `AppConfig`, `App.xaml.cs`, `SettingsWindow.xaml.cs`, and this status
file. The integrated result was rebuilt and retested during this audit: all 435 tests pass.

## What Ships Today

- **Launcher:** compact 3-pane folder/results/preview UI; fast global search with highlighting, body snippets,
  typo fallback, folder-aware ranking, favorites, recent responses, invalid-file surfacing, keyboard insert,
  plain insert, copy feedback, and a visible Edit action.
- **Paste:** configurable launcher hotkey (default **Ctrl+Alt+D**) plus per-response global hotkeys that paste
  rich or plain without opening the launcher. Focus returns to the prior app; clipboard content is restored;
  transient paste content is marked to stay out of Windows clipboard history.
- **Library/editor:** live-watched folder hierarchy; create/edit/duplicate/rename/move; rich editor with standard
  color/highlight palettes, lists, links, alignment, size, strike, tag chips, folder tags, recursive batch apply,
  edit-before-insert, templates, safe built-in variables, and response includes.
- **Safety/sharing:** import preview; zip/file import/export; conflict policies; invalid-file tolerance; importer and
  Trash path-traversal guards; rolling library backups with restore; app-local response Trash with restore.
- **Desktop integration:** System/Light/Dark themes, first-run onboarding, tray, startup registration, persisted
  settings/window placement, About/Diagnostics, rolling logs, large-library background/parallel scan, and
  self-contained single-file publishing.

## Remaining Work

Priority and acceptance detail live in [`BACKLOG.md`](BACKLOG.md). Current order:

1. **P0 - Fix import integrity (#8/#9):** align preview and import so Identical means no-op; validate/label invalid
   entries; add preview path containment; preserve folder-tag sidecars through ZIP import; finish duplicate/per-item decisions.
2. **P0 - Fix backup restore correctness (#10):** restoring the oldest retained snapshot can prune the selected
   file before it is opened, and replacement is delete-then-copy rather than atomic. Fix both with regression tests.
3. **P1 - Accessibility pass (#14):** not started. Only the launcher's seven icon controls have explicit
   automation names; newer dialogs and workflows still need full keyboard, screen-reader, high-contrast, focus,
   and non-color-only validation.
4. **P1 - Finish backup coverage (#10):** backups currently contain the library only and are automatic before
   import. Add config to snapshots and back up before batch tagging and any future bulk/major destructive edit.
5. **P2 - Finish Trash scope/settings (#11):** response soft-delete is complete. Folder/category deletion to
   Trash and a Settings control for retention (currently config-only, default 30 days; 0 keeps forever) remain.
6. **P2 - Complete logging coverage (#7):** add source/category metadata and verbose events for search, watcher,
   import/export, template resolution, theme, and each paste stage without logging customer content.
7. **P2 - Complete folder-tag UX (#8):** let the user choose direct children or recursive batch application.
8. **P2 - Refresh About/Diagnostics (#13):** the screen predates response hotkeys. Add actual launcher registration
   state, configured/registered/failed response-bind counts, and backup/Trash locations.

There are **no known open crash bugs** on `main`. The items above are incomplete requirements, not regressions in
the currently shipped paths.

## Known Limits / Human Checks

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
4. Run build + tests; expect **435** green. Registry tests need normal current-user registry access.
5. Take the first item in [`BACKLOG.md`](BACKLOG.md) on a new branch. Use ponytail, then separate-agent QA,
   both-theme screenshots, the four self-tests, a no-regression run, and a `--no-ff` merge.
6. Update this file, BACKLOG, README, and ARCHITECTURE as appropriate before considering the change done.
