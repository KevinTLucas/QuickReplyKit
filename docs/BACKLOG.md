# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## P2 - High-Value Enhancements

### B19 - Enterprise UI standards audit and polish

- **Severity:** P2 cross-view polish
- **Status:** In progress on `polish/enterprise-ui-audit`; independent visual/interaction and accessibility audits are
  active before any fixes are selected.
- **Impact:** Hover/focus/pressed chrome and icon-button behavior are not yet reviewed consistently across every view;
  for example, the Favorite star shows a box-like hover outline that may not suit an icon action.
- **Acceptance:** Launch and interact with every page/dialog using project QA tooling; capture representative Light,
  Dark, High Contrast, compact, and large states; inspect hover, pressed, selected, disabled, focus, keyboard, overflow,
  and long-text behavior against Windows enterprise-component conventions. Icon actions should use restrained,
  consistent chrome while retaining visible keyboard focus and accessible names. Fix only clear usability/consistency
  issues, run the full regression suite, and validate the portable release. Do not use GPT Computer Use.

- **Usage analytics / most-used sort:** local and opt-in only; only if Recent/Favorites prove insufficient.
- **Keyboard shortcut cheat sheet:** only after onboarding and accessibility validation show a discoverability gap.

## Explicitly Declined

- Natural/numeric name sorting and number-to-pick result selection: insufficient value for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, and AI-generated responses: outside the
  product brief. QuickReplyKit remains a local response launcher.
