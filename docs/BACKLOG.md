# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## Active Work

### B20 - Import workflow crash

- **Severity / status:** P0 · Confirmed · Investigation in progress
- **Problem:** an import failure can escape the UI workflow and terminate the application.
- **Acceptance:** every import-stage failure is caught at the UI boundary, produces a useful safe error, is recorded in
  diagnostics without customer content, leaves the current library/config intact, and keeps QuickReplyKit usable.
  Regression coverage must exercise malformed, inaccessible, stale, and failed-write inputs.

### B21 - Explicitly trusted native scripts

- **Severity / status:** P1 · Requested · Queued after B20
- **Problem:** `qrk-expr-1` intentionally cannot read files or perform the internal data-processing workflows now
  requested by users.
- **Acceptance:** add an explicitly identified native script type that can access local files, parse data, and return
  response content. Execution must be out of process, bounded by timeout/output limits, cancellable, and isolated from
  the app process so syntax/runtime failures cannot crash or hang QuickReplyKit. Trust remains local and source-bound;
  imported native code displays its exact source and requires individual acceptance with no bulk-accept path. Document
  clearly that approved native scripts have the same access as the signed-in Windows user.

### B22 - Per-user default paths and truthful diagnostics

- **Severity / status:** P1 · Requested · Queued
- **Acceptance:** defaults resolve from the installed user's Windows profile: library and backup locations under the
  intended user-profile folders; config, logs, Trash, and script trust under `%APPDATA%\QuickReplyKit`. About and
  Diagnostics display the actual resolved filesystem paths rather than environment-variable or privacy placeholders.
  Configured custom locations remain supported and upgrades do not silently move existing data.

### B23 - About / Diagnostics window chrome cleanup

- **Severity / status:** P2 · Requested · Queued
- **Acceptance:** remove the redundant in-content Close button from About / Diagnostics; the standard window close
  control remains keyboard- and pointer-accessible, with no layout gap.

### B24 - Remove shortcut-combination lookup

- **Severity / status:** P2 · Requested · Queued
- **Acceptance:** remove “Find a Shortcut by Combination” because capture is unreliable while global shortcuts are
  registered. Keep the normal hotkey search field and conflict indicators; delete only code/tests used exclusively by
  the removed capture workflow.

### B25 - Reorganize backup and Trash settings

- **Severity / status:** P2 · Requested · Queued
- **Acceptance:** remove the Backup Folder Reset button and its unused handler. Place Backup/Restore Library and Trash
  controls directly beneath the Backup Folder settings, preserving validation, keyboard access, compact resizing, and
  both themes.

### B26 - Short scoped-search label

- **Severity / status:** P2 · Requested · Queued
- **Acceptance:** the active scoped-search chip shows only the selected folder's display name, not the full hierarchy;
  search behavior and the full underlying category path remain unchanged.

### B27 - Background-only selected-folder state

- **Severity / status:** P2 · Requested · Queued
- **Acceptance:** remove the bar-style selected-folder indicator. Selection is communicated through a subtle but
  visible background in Light and Dark themes plus the existing non-color cue; preserve High Contrast accessibility.

### B28 - Icon-only Import / Export menu trigger

- **Severity / status:** P2 · Requested · Queued
- **Acceptance:** remove the visible “Import / Export” label from the launcher trigger and retain the existing icon,
  tooltip, automation name, keyboard access, and menu behavior without increasing compact-window clutter.

### B29 - Rename transient response editing action

- **Severity / status:** P2 · Requested · Queued
- **Acceptance:** rename the response context-menu action currently shown as “Edit & insert…” to “Edit Response Text…”
  (or equally clear wording) without changing its transient compose-and-insert behavior.

## Deferred Ideas

- **Usage analytics / most-used sort:** local and opt-in only; only if Recent/Favorites prove insufficient.
- **Keyboard shortcut cheat sheet:** only after onboarding and accessibility validation show a discoverability gap.

## Explicitly Declined

- Natural/numeric name sorting and number-to-pick result selection: insufficient value for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, and AI-generated responses: outside the
  product brief. QuickReplyKit remains a local response launcher.
