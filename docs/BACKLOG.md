# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## Active Work

- **B35 · P1 · QA — Microsoft Word rich-paste fidelity.** Current `main` can let Word select the plain-text clipboard
  representation before RTF, which drops inline formatting. `feature/word-style-word-fidelity` publishes RTF → HTML
  → Unicode, skips empty RTF, and uses the editor's normal 14-unit size. Automated QA is green; a manual paste into
  Microsoft Word remains before merge because Word is not installed on this machine.
- **B36 · P1 · QA — rich editor formatting persistence.** The editor's native WPF bold, italic, and underline
  commands stored formatting on text-run properties that the HTML serializer ignored, so formatting looked correct
  until Save/reopen. `bugfix/rich-editor-persistence` normalizes loaded and newly edited formatting at text runs,
  preserves independent underline/strikethrough state, fixes Automatic color/Clear/aligned-list behavior, and uses
  the same safe-link schemes on edit and reload. Release build is clean and 805 tests pass; independent QA remains.

## Deferred Ideas

- **Usage analytics / most-used sort:** local and opt-in only; only if Recent/Favorites prove insufficient.
- **Keyboard shortcut cheat sheet:** only after onboarding and accessibility validation show a discoverability gap.

## Explicitly Declined

- Natural/numeric name sorting and number-to-pick result selection: insufficient value for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, and AI-generated responses: outside the
  product brief. QuickReplyKit remains a local response launcher.
