# QuickReplyKit - Project Status

> Current-state source of truth. Read this after [`../CLAUDE.md`](../CLAUDE.md). Update it with every
> meaningful change; a change is not complete until this file agrees with the code, tests, and backlog.

## Audited Baseline

- **Last audited:** 2026-09-07
- **Current `main` before this process-doc update:** `97e4ff8`; product merge `c596e29`.
- **Build:** clean, 0 warnings / 0 errors (`dotnet build QuickReplyKit.slnx -c Debug`).
- **Tests:** **443 passed, 0 failed, 0 skipped** after B02 integration QA.
- **Windows self-tests:** `--selftest-paste`, `--selftest-paste-rich`, `--selftest-paste-ui`, and
  `--selftest-foreground` all returned exit code 0.
- **Git:** completed feature branches listed below are ancestors of `main`. The only unmerged work is the isolated
  B02 and B03 bugfix branches described under Active worktrees. Several fully merged historical branches/worktrees
  remain locally and may be pruned after confirming no agent still owns them.
- **State:** launchable, but a new user-requested backlog wave is active. [`BACKLOG.md`](BACKLOG.md) now contains
  15 ordered items with severity, acceptance criteria, and status. Confirmed data-loss/import defects lead;
  sandboxed scripting is active but deliberately last behind its security prerequisites.

## Merged And QA'd

| Backlog item | Merge | Verification recorded in merge history | State |
|---|---:|---|---|
| B02 transactional backup restore | `c6fdda8` | 443/443; rollback/oldest restore 40 repeated runs; all self-tests 3 rounds; both themes | Complete |
| B01 repeated right-click crash | `c596e29` | 436/436; 150 rapid cycles per theme; actions usable; log delta clean | Complete |
| #1 dark-mode Category ComboBox + original #6 right-click fix | `d562052` | 6/6 QA; both themes; original reentrancy path survived 80 rapid clicks | Original fix; later Run crash closed by B01 |
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
file. The integrated result was rebuilt and retested during that audit at 435 tests; B01 adds the 436th test.

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

Priority and acceptance detail live in [`BACKLOG.md`](BACKLOG.md). Immediate order:

1. **B03 P0:** fix import preview/import containment, invalid/identical/duplicate plan integrity.
2. **B04-B07 P1:** clipboard sanitization, search relevance, Settings/About layout, and dark hover contrast.
3. **B08-B11 P1:** folder Trash, remove folder tags, scoped-search chip, and full accessibility.
4. **B12-B15 P2:** clearer Import/Export UI, configurable backup path, logging, and diagnostics completion.
5. **B16 P2:** sandboxed dynamic-variable scripts, only after its import, sanitizer, accessibility, and logging gates.

**Known confirmed regressions/defects:** import preview/import disagreement and preview containment, search false
positives, dark hover contrast, and clipped
Settings/About layout. Treat them as bugs, not optional polish.

**Active worktrees:** B02 is complete and ready to merge; its worktree can be removed afterward.
`bugfix/import-integrity` contains the committed B03 validated-plan implementation and awaits security/UI QA;
`bugfix/dark-hover-contrast` and `bugfix/search-relevance` contain their isolated P1 fixes and await QA. Resume these
exact worktrees rather than starting duplicates.
Each branch's `main...branch` diff is scoped to its named bug; inherited commits in `git log branch` are not branch-owned
work. Branch names must stay human-readable and must not contain AI/vendor names, IDs, or generated session labels.

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
4. Run build + tests; expect **436** green. Registry tests need normal current-user registry access.
5. Take the first item in [`BACKLOG.md`](BACKLOG.md) on a new branch. Use ponytail, then separate-agent QA,
   both-theme screenshots, the four self-tests, a no-regression run, and a `--no-ff` merge.
6. Update this file, BACKLOG, README, and ARCHITECTURE as appropriate before considering the change done.
