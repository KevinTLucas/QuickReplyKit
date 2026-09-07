# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## P1 - Core Workflow And Major UI

### B04 - Sanitize Create from Clipboard content

- **Severity:** P1 (trust/content safety)
- **Status:** Queued
- **Impact:** Clipboard HTML can carry scripts, event attributes, embedded objects, unsafe links, or editor-specific
  markup that should never enter a response.
- **Acceptance:** Clipboard input is parsed/normalized into the supported HTML subset only; scripts, stylesheets,
  comments, forms, iframes/objects, images, event attributes, unsafe URI schemes, hidden content, and unsupported
  markup are removed. Legitimate visible `<code>`/`<pre>` snippets remain as safely encoded visible text; allowed
  formatting and https/mailto links remain; plain text is encoded; malformed input never crashes; adversarial
  sanitizer tests cover active content and URL schemes.

### B06 - Repair Settings layout and About area

- **Severity:** P1
- **Status:** Confirmed UI defect
- **Impact:** Settings content, especially the About/Diagnostics area, is truncated or broken at real display scales.
- **Acceptance:** Settings and About are fully visible at 100/150/200% scaling, common screen heights, both themes,
  and High Contrast; no clipped text/buttons; content scrolls when necessary; logical grouping and keyboard tab order;
  screenshots and UI Automation verify every control remains reachable.

### B08 - Delete and restore folders through Trash

- **Severity:** P1 (data safety)
- **Status:** Queued; response Trash already exists
- **Impact:** Users cannot delete/restore a category and its nested responses as one recoverable operation.
- **Acceptance:** Folder context menu offers Delete; confirmation states folder name and nested response count;
  deletion creates a required backup then moves the entire subtree to app Trash; restore recreates the exact hierarchy
  and side files without overwrite; conflicts are explained and leave Trash intact; delete-forever/retention work for
  folders; empty/root/symlink/reparse/path-escape cases are safe; live library refreshes after delete/restore.

### B09 - Remove folder tags

- **Severity:** P1 (requested product correction)
- **Status:** Queued
- **Impact:** Folder metadata adds complexity the user no longer wants. Folders should only organize or bulk-edit
  contained responses; response tags remain.
- **Acceptance:** Remove Folder tags and Apply folder tags controls; stop reading/writing/applying folder tags; remove
  dead folder-tag model/tests/code; legacy `.qrkfolder.json` files are ignored safely and never appear as responses;
  response tag chips and search remain; direct/ZIP sharing does not treat legacy sidecars as responses.

### B10 - Outlook-style folder-scoped search chip

- **Severity:** P1
- **Status:** Queued
- **Impact:** Users need visible, predictable folder filtering while typing a global search.
- **Acceptance:** Selecting a folder adds a removable chip in/adjacent to the search field and limits results to that
  folder subtree; selecting another folder replaces it; removing the chip or selecting All Responses returns global
  scope; Favorites/Recent behavior is explicit; chip is keyboard-removable and screen-reader named; empty/fuzzy/body
  searches honor scope; compact resizing does not overlap controls.

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

### B13 - Configurable backup-folder location

- **Severity:** P2
- **Status:** Queued; implement after the P0 B02 correctness fix
- **Impact:** Enterprises may require backups on a managed/local/network path rather than `%APPDATA%`.
- **Acceptance:** Settings shows current backup location with Change/Open/Reset; value persists; default remains app
  data; path is validated and writable before save/use; unavailable paths fail clearly without blocking normal app
  startup; backup/restore consistently use the configured path; About reports it; tests cover defaults, persistence,
  migration, invalid/unavailable paths, and restore.

### B14 - Complete diagnostics logging coverage

- **Severity:** P2
- **Status:** Confirmed remaining gap; foundation shipped in `8f74aa9`
- **Impact:** Existing rolling/crash logs omit source/category and several workflows needed for support diagnosis.
- **Acceptance:** Add source/category metadata and verbose events for search, watcher, import/export, template resolve,
  theme changes, context-menu failures, and each paste stage; continue excluding response bodies, clipboard content,
  and customer data; keep async bounded rollover and normal/verbose toggle.

### B15 - Complete About / Diagnostics status

- **Severity:** P2
- **Status:** Confirmed remaining gap; foundation shipped in `23468f7`; coordinate with B06/B13/B14
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
