# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## Active Work

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
