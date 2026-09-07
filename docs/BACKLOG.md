# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## P1 - Core Workflow And Major UI

### B17 - Redesign import confirmation and selection

- **Severity:** P1 core sharing workflow
- **Status:** Queued after B15; coordinate code-bearing rows with B16
- **Impact:** The current import confirmation is hard to scan and control for a large shared folder.
- **Acceptance:** Replace it with a clear scrollable list whose ordinary response rows have checkboxes, useful
  New/Changed/Duplicate/Conflict status text, Select all/Clear actions, and a concise selected-count summary. Preserve
  per-conflict Keep/Overwrite/Keep both decisions without clutter. Code-bearing dynamic-variable definitions must show
  exact source and require explicit per-item acceptance; Select all and any bulk/yes-to-all action must never accept
  code. Validate large mixed imports, resizing, keyboard use, both themes, High Contrast, and import-plan integrity.

## P2 - High-Value Enhancements

### B15 - Complete About / Diagnostics status

- **Severity:** P2
- **Status:** Confirmed remaining gap; foundation shipped in `23468f7`; B13/B14 prerequisites complete
- **Impact:** Support snapshot predates response hotkeys, backups, and Trash.
- **Acceptance:** Report actual launcher registration, configured/registered/failed response-hotkey counts, library and
  watcher state, config/log/backup/Trash paths, version/OS/runtime, and logging mode; copyable and safe to attach; no
  response/clipboard/customer content; all Open buttons work and layout passes B06.

### B16 - Sandboxed user scripts for dynamic variables

- **Severity:** P2 feature; security-critical design
- **Status:** Queued last; B04/B11/B14 prerequisites complete
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

## P3 - Optional After Requested Work

### B18 - Stronger selected-folder indication

- **Severity:** P3 visual navigation polish
- **Status:** Queued before B19
- **Impact:** The selected folder in the left navigation is not always visually obvious at a glance.
- **Acceptance:** Add a clear theme-safe selected state using existing resources plus a non-color cue so it remains
  understandable in High Contrast and to screen readers. Avoid relying on yellow alone. Validate deep folders,
  keyboard selection, focused/inactive states, resizing, and both themes.

- **Usage analytics / most-used sort:** local and opt-in only; only if Recent/Favorites prove insufficient.
- **Keyboard shortcut cheat sheet:** only after onboarding and accessibility validation show a discoverability gap.

## Explicitly Declined

- Natural/numeric name sorting and number-to-pick result selection: insufficient value for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, and AI-generated responses: outside the
  product brief. QuickReplyKit remains a local response launcher.
