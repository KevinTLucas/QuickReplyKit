# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## Active Work

### B21 - Explicitly trusted native scripts

- **Severity / status:** P1 · In progress
- **Problem:** `qrk-expr-1` intentionally cannot read files or perform the internal data-processing workflows now
  requested by users.
- **Acceptance:** add an explicitly identified native script type that can access local files, parse data, and return
  response content. Execution must be out of process, bounded by timeout/output limits, cancellable, and isolated from
  the app process so syntax/runtime failures cannot crash or hang QuickReplyKit. Trust remains local and source-bound;
  imported native code displays its exact source and requires individual acceptance with no bulk-accept path. Document
  clearly that approved native scripts have the same access as the signed-in Windows user.

### B26 - Short scoped-search label

- **Severity / status:** P2 · QA
- **Acceptance:** the active scoped-search chip shows only the selected folder's display name, not the full hierarchy;
  search behavior and the full underlying category path remain unchanged.
- **Implemented on:** `ui/scoped-search-label`; independent QA passes a clean Release build, 731/731 tests, and
  Light/Dark/compact captures. The visual label uses the folder leaf while accessibility retains the full path.
  Ready for merge.

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

### B30 - Sample dynamic variables and scripts

- **Severity / status:** P3 · Requested · Queued last
- **Acceptance:** after every higher-priority item is complete, add a small set of clearly named, safe examples that
  demonstrate built-in variables, response includes, the restricted expression language, and explicitly trusted
  PowerShell. Include the requested randomized greeting example with ten greetings and an added “Happy Friday” line
  on Fridays. Examples must be optional seed content, contain no user-specific paths or data, explain the trust model,
  and remain easy to delete or copy into a real library.

## Deferred Ideas

- **Usage analytics / most-used sort:** local and opt-in only; only if Recent/Favorites prove insufficient.
- **Keyboard shortcut cheat sheet:** only after onboarding and accessibility validation show a discoverability gap.

## Explicitly Declined

- Natural/numeric name sorting and number-to-pick result selection: insufficient value for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, and AI-generated responses: outside the
  product brief. QuickReplyKit remains a local response launcher.
