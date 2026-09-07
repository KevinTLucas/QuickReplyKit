# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## P1 - Core Workflow And Major UI

### B11 - Full accessibility pass

- **Severity:** P1
- **Status:** Queued
- **Impact:** New dialogs and workflows do not yet meet the requested keyboard, screen-reader, High Contrast, and
  non-color-only requirements.
- **Acceptance:** Every action is mouse-free with predictable tab order, visible focus, defaults, and Escape; useful
  automation names/roles/states cover launcher, results, preview, editor, tags, import, backup, Trash, onboarding,
  About, and hotkeys; High Contrast is usable; status never relies on color alone; ComboBox Slide animation is removed
  or respects reduced motion; keyboard/UIA/both-theme/High-Contrast/DPI validation passes.

## P2 - High-Value Enhancements

### B12 - Discoverable Import and Export controls

- **Severity:** P2
- **Status:** Queued
- **Impact:** The `...` control hides important sharing actions.
- **Acceptance:** Replace it with a clearly labelled/icon-supported Import/Export entry point that exposes Import,
  Export response/folder/library in context; remains compact at minimum width; accessible name/tooltip and keyboard
  access are obvious; no duplicate or ambiguous commands.

### B14 - Complete diagnostics logging coverage

- **Severity:** P2
- **Status:** Confirmed remaining gap; foundation shipped in `8f74aa9`
- **Impact:** Existing rolling/crash logs omit source/category and several workflows needed for support diagnosis.
- **Acceptance:** Add source/category metadata and verbose events for search, watcher, import/export, template resolve,
  theme changes, context-menu failures, and each paste stage; continue excluding response bodies, clipboard content,
  and customer data; keep async bounded rollover and normal/verbose toggle.

### B15 - Complete About / Diagnostics status

- **Severity:** P2
- **Status:** Confirmed remaining gap; foundation shipped in `23468f7`; coordinate with B13/B14
- **Impact:** Support snapshot predates response hotkeys, backups, and Trash.
- **Acceptance:** Report actual launcher registration, configured/registered/failed response-hotkey counts, library and
  watcher state, config/log/backup/Trash paths, version/OS/runtime, and logging mode; copyable and safe to attach; no
  response/clipboard/customer content; all Open buttons work and layout passes B06.

### B16 - Sandboxed user scripts for dynamic variables

- **Severity:** P2 feature; security-critical design
- **Status:** Queued last after B04/B11/B14
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

## P3 - Optional After Requested Work

- **Usage analytics / most-used sort:** local and opt-in only; only if Recent/Favorites prove insufficient.
- **Keyboard shortcut cheat sheet:** only after onboarding and accessibility validation show a discoverability gap.

## Explicitly Declined

- Natural/numeric name sorting and number-to-pick result selection: insufficient value for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, and AI-generated responses: outside the
  product brief. QuickReplyKit remains a local response launcher.
