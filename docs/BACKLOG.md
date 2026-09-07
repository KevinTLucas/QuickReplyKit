# QuickReplyKit - Open Backlog

Only unfinished work belongs here. Completed implementation and QA history is summarized in
[`STATUS.md`](STATUS.md) and preserved in Git. Prioritize data safety and accessibility before optional ideas.

## Requested Work

### P0 - Fix import preview/import integrity (original #8/#9)

The 2026-09-07 independent audit found four correctness/security gaps in the merged flow:

- **Identical is not a no-op.** Preview labels same-path, byte-identical files as Identical/skipped, but
  `LibraryPorter.Import` applies the selected conflict policy to every existing destination. Overwrite rewrites the
  file and Keep both creates a duplicate. Make import consume the preview plan or independently skip identical files.
- **Preview lacks path containment.** A crafted ZIP entry can make `ImportPreview.Analyze` probe/read outside the
  library before confirmation. Apply the same canonical containment rule used by `LibraryPorter.Import` before any
  destination read.
- **ZIP import drops folder tags.** Export includes `.qrkfolder.json`, but import treats it as a response, fails
  `ResponseFile.Load`, and skips it. Import the sidecar through a separately validated metadata path. Direct folder
  copying already preserves it.
- **Invalid entries are mislabelled.** Preview classifies every `.json` by destination bytes without response
  validation, while the importer later rejects invalid responses. Preview invalid entries explicitly and keep valid
  siblings importable.

Then finish the originally requested review UX: detect same-content duplicates at another path and allow per-item
decisions for meaningful conflicts while retaining apply-to-all for ordinary responses. Add adversarial ZIP tests and
an export-folder -> import -> folder-tags round trip.

**Future code security gate:** if executable variables are ever added, show the exact imported code and require
explicit acceptance for every code-bearing item. No Yes-to-all for code. This is blocked/not applicable while
templates remain the fixed, non-executable allow-list.

### P0 - Fix backup restore correctness (original #10)

The merged restore has two audit-found bugs:

- `Restore` creates/prunes a pre-restore snapshot before opening the selected ZIP. Selecting the oldest of the 10
  retained backups can prune that same file and make restore fail. Protect/copy the selected archive before pruning.
- Replacement is delete-then-copy after extraction, not atomic. A copy failure can leave a partially restored
  library. Stage and swap safely, with rollback when replacement fails.

Add regression tests for restoring the oldest retained backup and a forced replacement failure.

### P1 - Full accessibility pass (original #14)

Not started. Go beyond resize/dark mode and the launcher's existing basic automation names:

- Make every action and dialog operable without a mouse, with predictable tab order, visible focus, and Escape /
  default-button behavior.
- Add useful screen-reader names, labels, roles, and state to launcher results, navigation, preview, editor toolbar,
  tag chips, import preview, backup, Trash, onboarding, About, and hotkey dialogs.
- Validate Windows High Contrast. Never use color alone for invalid/import/conflict/favorite/match state; pair it
  with text or a shape. Respect reduced motion; the current themed ComboBox uses a Slide popup animation and needs
  reduced-motion/High-Contrast-safe behavior.
- QA with keyboard-only walkthroughs, UI Automation inspection, both themes, High Contrast, resize/DPI checks, and
  all four paste self-tests.

### P1 - Complete automatic backup coverage (original #10)

Partially shipped in `f303524`: rolling library zip snapshots (10 retained), Restore Library, manual backup,
and automatic backup before import.

Remaining:

- Include `%APPDATA%\QuickReplyKit\config.json` in a restorable snapshot, without breaking old library-only backups.
- Add a size and/or age retention ceiling; count-only retention can still consume unbounded space with large libraries.
- Create a backup before recursive folder-tag batch apply. Apply the same rule to any future bulk move/delete or
  major multi-response edit.
- Make backup failure visible before a risky operation; do not silently continue when the safety copy was required.

### P2 - Complete diagnostics logging coverage (original #7)

Partially shipped in `8f74aa9`: async level-gated rolling logs, session headers, crash capture, Settings toggle, and
Open logs actions are complete. Logging is bounded and current call sites avoid response bodies.

Remaining from the original request:

- Add source/category metadata to log entries.
- In verbose mode, cover search, watcher events, import/export, template resolution, theme changes, and each paste
  stage. Continue logging only identifiers/steps and errors, never response bodies, clipboard content, or customer data.

### P2 - Complete folder-tag batch UX (original #8)

The merged UI always applies tags recursively. Let the user choose **direct children** or **this folder and all
subfolders** before confirmation. Keep union/dedupe behavior and add tests for both scopes. The ZIP sidecar loss is
the higher-priority import-integrity item above.

### P2 - Complete in-app Trash (original #11)

Partially shipped in `ef5a573`: response delete moves to app-local Trash; users can restore, delete forever, or empty;
restore refuses conflicts and path escapes; startup purge honors `TrashRetentionDays`.

Remaining:

- Add folder/category deletion to Trash, preserving the full subtree for one-step restore.
- Add a Settings retention control. The persisted setting exists today (default 30 days; `0` = keep forever) but is
  not editable in the UI.
- Ensure any folder delete gets an automatic backup first and is covered by live delete/restore/conflict tests.

### P2 - Refresh About / Diagnostics (original #13)

The shipped screen (`23468f7`) reports version, runtime/OS, launcher hotkey, paste mode, theme, verbose logging,
library/count/watcher state, config, and logs. It predates per-response hotkeys and later safety features.

Add the launcher's **actual Windows registration state**, configured/registered/failed response-bind counts, plus
backup and Trash paths. Keep it read-only, copyable, and safe to attach to a support ticket; never include response
bodies, clipboard data, or other customer content.

## Future Ideas - Start Only When The User Explicitly Asks

- **Sandboxed user scripting for variables.** Current variables are a fixed safe allow-list with no code execution.
  Any scripting design needs a real sandbox/capability model and cannot ship before the import hard-acceptance gate
  above. Never raw-eval code from a shareable response file.
- **Usage analytics / most-used sort.** Local and opt-in only; worthwhile only if actual usage shows Recent/Favorites
  are insufficient.
- **Keyboard shortcut cheat sheet.** Small discoverability overlay after the accessibility pass if onboarding and
  tooltips still prove insufficient.

## Explicitly Declined

- Natural/numeric name sorting and number-to-pick result selection: insufficient value for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, and AI-generated responses: outside the
  product brief. QuickReplyKit remains a local, focused response launcher.
