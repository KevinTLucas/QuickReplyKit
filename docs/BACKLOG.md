# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## Active Work

### B31 - Whole-app UI and interaction consistency pass

- **Severity / status:** P2 · Requested · Queued second-to-last
- **Acceptance:** inspect every user-facing window, menu, context menu, dialog, toolbar, button, icon, tooltip, hover,
  pressed, disabled, selected, and keyboard-focus state in Light, Dark, compact, and normal layouts. Exercise the real
  workflows rather than reviewing screenshots in isolation. Fix only small, low-risk inconsistencies that improve
  clarity or consistency, such as an isolated border, mismatched hover color, unclear icon state, spacing drift, or
  missing accessible label. Preserve native High Contrast behavior and avoid broad redesigns or scope expansion.
  Validate the resulting UI with representative screenshots and focused interaction tests.

### B32 - End-user GitHub README and complete visual guide

- **Severity / status:** P2 · Requested · Queued last
- **Acceptance:** make the root `README.md` a polished end-user landing page that presents QuickReplyKit clearly and
  documents every shipped workflow: installation and portable distribution, first run, launcher hotkey, search and
  folder navigation, preview, rich/plain paste and copy, instant response hotkeys, creating/editing/organizing
  responses, tags, variables/includes/scripts and trust, import/export review, backups/restore, Trash, themes,
  settings, tray, diagnostics/logs, troubleshooting, and accessibility/keyboard use. Include current, readable
  screenshots from the shipping UI in both themes where useful. Move detailed build, architecture, dependency, and
  contributor material into appropriately named internal docs without losing it. Keep the README easy to scan,
  accurate for the final executable, and attractive on GitHub.

## Deferred Ideas

- **Usage analytics / most-used sort:** local and opt-in only; only if Recent/Favorites prove insufficient.
- **Keyboard shortcut cheat sheet:** only after onboarding and accessibility validation show a discoverability gap.

## Explicitly Declined

- Natural/numeric name sorting and number-to-pick result selection: insufficient value for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, and AI-generated responses: outside the
  product brief. QuickReplyKit remains a local response launcher.
