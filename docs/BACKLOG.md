# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## Active Work

### B32 - End-user GitHub README and complete visual guide

- **Severity / status:** P2 · Documentation · QA
- **Acceptance:** make the root `README.md` a polished end-user landing page that presents QuickReplyKit clearly and
  documents every shipped workflow: installation and portable distribution, first run, launcher hotkey, search and
  folder navigation, preview, rich/plain paste and copy, instant response hotkeys, creating/editing/organizing
  responses, tags, variables/includes/scripts and trust, import/export review, backups/restore, Trash, themes,
  settings, tray, diagnostics/logs, troubleshooting, and accessibility/keyboard use. Include current, readable
  screenshots from the shipping UI in both themes where useful. Move detailed build, architecture, dependency, and
  contributor material into appropriately named internal docs without losing it. Keep the README easy to scan,
  accurate for the final executable, and attractive on GitHub.
- **Implementation:** branch `end-user-readme` contains the end-user-first guide, 10 curated shipping-UI screenshots
  plus the app icon, a separate maintainer guide at [`DEVELOPMENT.md`](DEVELOPMENT.md), narrow documentation-image
  tracking, and corrected
  documentation links/completion boundaries. Local links, image decoding, screenshot privacy, Markdown structure,
  Release build, and all 785 tests pass; it awaits independent editorial/link/image QA before merge.

## Deferred Ideas

- **Usage analytics / most-used sort:** local and opt-in only; only if Recent/Favorites prove insufficient.
- **Keyboard shortcut cheat sheet:** only after onboarding and accessibility validation show a discoverability gap.

## Explicitly Declined

- Natural/numeric name sorting and number-to-pick result selection: insufficient value for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, and AI-generated responses: outside the
  product brief. QuickReplyKit remains a local response launcher.
