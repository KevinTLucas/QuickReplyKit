# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## P2 - High-Value Enhancements

### B16 - Sandboxed user scripts for dynamic variables

- **Severity:** P2 feature; security-critical design
- **Status:** Core foundation security QA complete on `feature/sandboxed-script-engine` (99 focused, 671 integrated);
  runtime/template integration is active on `feature/sandboxed-script-runtime`, and secure import/review integration is
  active on `feature/sandboxed-script-import`. Script-manager UI follows those two branches.
- **Impact:** Enables custom computed template values, but shareable executable content creates a major trust boundary.
- **Acceptance:** Define the full user workflow: create, name, edit, validate, disable, delete, export, and review
  scripts; persist definitions in a documented format; invoke them with explicit template syntax such as
  `{{script:Name}}`; resolve consistently in preview, rich paste, plain paste, copy, and edit-before-insert.
  Never execute imported code in the main UI process and never raw-eval response content. Use either OS-enforced
  isolation with an explicit threat model or a deliberately non-general-purpose interpreter whose operations are
  intrinsically bounded; a separate process alone is not a sandbox. Fail closed when containment is unavailable.
  Enforce time, memory, process, filesystem, registry, environment, clipboard, UI, network, recursion, and output-size
  limits with default-deny capabilities; failures become visible non-crashing markers. Import preview displays exact
  code and requires per-item acceptance with **no accept-all for code-bearing items**; accepted-code provenance is
  local, invalidated when code changes, and never travels as trust. Security QA must prove containment cannot be
  bypassed through path, network, process, environment, reflection/native loading, recursion, timeout, or output abuse.

### B19 - Enterprise UI standards audit and polish

- **Severity:** P2 cross-view polish
- **Status:** Queued next-to-last, after all feature UI and before final release validation
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
