# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## Active Work

- **B39 · P1 · In progress — self-test Notepad startup/cleanup race.** On a slow or foreground-limited host, the paste
  self-tests can time out and delete their temporary file before Windows 11 Notepad finishes opening it, leaving one
  or more “Not a valid file name” windows. Make ownership/startup deterministic and guarantee cleanup of the exact
  test-created window without touching user Notepad sessions. `fix/selftest-target-cleanup` replaces Notepad with an
  exact, owned child-process edit target and is awaiting independent QA.
- **B35 · P1 · QA — Microsoft Word rich-paste fidelity.** Current `main` can let Word select the plain-text clipboard
  representation before RTF, which drops inline formatting. `feature/word-style-word-fidelity` publishes RTF → HTML
  → Unicode, skips empty RTF, and uses the editor's normal 14-unit size. Automated QA is green; a manual paste into
  Microsoft Word remains before merge because Word is not installed on this machine.

## Deferred Ideas

- **Usage analytics / most-used sort:** local and opt-in only; only if Recent/Favorites prove insufficient.
- **Keyboard shortcut cheat sheet:** only after onboarding and accessibility validation show a discoverability gap.

## Explicitly Declined

- Natural/numeric name sorting and number-to-pick result selection: insufficient value for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, and AI-generated responses: outside the
  product brief. QuickReplyKit remains a local response launcher.
