# Backlog — future ideas

Everything previously tracked here is **done and shipped** (the MVP robustness items and all V2 features —
richer editor, edit-before-insert, template variables, response includes, dark mode — plus the end-user
continuous-improvement pass). See [`STATUS.md`](STATUS.md) for the current state and known cosmetic caveats.

## Requested — queued to build (user, 2026-09-06)

Prioritized; a bug first. Follow the normal flow (branch → ponytail → separate-subagent QA → merge; update docs).

1. ✅ **DONE (merged `d562052`)** — Dark-mode Category ComboBox readability. App-wide themed `ComboBox`/`ComboBoxItem`
   `ControlTemplate` in `App.xaml` bound to theme brushes via `DynamicResource` (covers the editor Category picker and
   the Move/Rename dialog). Independent QA: readable in both themes, closed field + open dropdown. See STATUS.md.
2. **Standardize the editor text-color & highlight palettes to common colors.** The editor already has a text-color
   (**A**) button and a highlight swatch popup (Phase 10a). Refine both to a **standard, common-color** set — e.g.
   text: black / gray / red / orange / green / blue / purple (+ "automatic/theme default"); highlight: yellow / green
   / cyan / pink / orange / gray (+ none). Keep the swatches discoverable and legible in both themes (see the known
   caveat: a highlight with no explicit text color can be low-contrast in dark mode — consider pairing a sensible
   dark text color when a light highlight is applied).
3. **Make "Edit" discoverable.** Editing an existing response already works via **right-click → Edit** (reuses the
   editor popup, pre-filled) — but it's hidden. Surface it more visibly: e.g. an **edit (✎) button** on the selected
   result row or in the preview action bar, opening the same editor. (Also expose "Edit & insert".) No new editor
   needed — just a visible entry point to the existing `EditorWindow.OpenEdit`.
4. **Per-response instant-paste hotkeys (no launcher).** Let a user bind a **global hotkey to a specific response**
   that pastes it **directly at the cursor without opening the app**, as **rich or plain** (choose per bind). Reuses
   the existing paste engine (`ForegroundMonitor` target + `InsertService` + history-exclusion + clipboard restore) —
   the hotkey handler resolves the response's templates and inserts, no window shown. **Design decisions to make:**
   where the bind is stored — in the response `.json` (travels when shared, but risks cross-user conflicts) vs. a
   central `config.json` map (local, safer) — recommend **local config map** by relative path, so shared libraries
   don't fight over global keys; register each via `RegisterHotKey` at startup and on change; fail gracefully when a
   gesture is already taken (surface it in the manager, item #5). Set the bind from the response's context menu /
   editor ("Assign hotkey…").
5. **Hotkey manager + conflict detection + "search by key" (Settings).** A dedicated keybinds page listing **every**
   assigned hotkey — the global launcher hotkey **and** all per-response binds — with each bind's gesture + target
   (launcher / response name) + rich/plain, and edit/clear. It must clearly flag conflicts: (a) **duplicate gestures
   within the app** (block/warn before saving), and (b) **gestures rejected by Windows** because another app/OS owns
   them (detectable: `RegisterHotKey` returns false → mark that bind "in use, pick another", don't crash).
   **Search-by-key (like a game/modpack controls screen):** a capture field where the user **presses a key combo** and
   the page instantly shows what (if anything) it's already bound to — so they can check a combo is free before
   assigning, and jump to any conflict. Also support plain text filtering of the list by gesture or response name.
   This is the "strong ability to view keybinds and find conflicts" ask; build it alongside #4.
6. ✅ **DONE (merged `d562052`)** — Fast/repeated right-click crash. Root cause was mutating `ContextMenu.IsOpen` inside
   the `Opened` handler (reentrancy). Both the results and nav menus now gate in `ContextMenuOpening` via `e.Handled`;
   no `IsOpen` mutation remains. Independent QA: survived 80 right-clicks @15ms with no crash. See STATUS.md.
7. ✅ **DONE (merged `8f74aa9`)** — Diagnostics/logging system. Hand-rolled thread-safe `Logger` (Core/Diagnostics)
   + `Diag` facade: async single-writer, level-gated (Error/Warn/Info/Debug/Verbose), size-capped rolling (~10 MB/file,
   keep 5 → ~50 MB) under `%APPDATA%\QuickReplyKit\logs\`; session header; crash capture (AppDomain/Dispatcher/Task,
   logged-not-swallowed); Settings verbose toggle (live) + "Open logs folder" (Settings + tray). Independent QA 9/9
   incl. security (no body/PII leak). Original spec kept below for reference.
   <details><summary>original requirements</summary>
   - **Rolling & size-capped (~50 MB total)** — never grows unbounded; size-based rollover (a few files) or a single
     capped/circular file. Logs under `%APPDATA%\QuickReplyKit\logs\`.
   - **Standard levels** (Error/Warn/Info/Debug/Verbose), timestamped, with thread + source/category; each **session**
     delimited by a header (app version, OS, key config). Async/queued writes so it never blocks the UI.
   - **Captures crashes automatically:** wire `AppDomain.CurrentDomain.UnhandledException`,
     `Application.DispatcherUnhandledException`, and `TaskScheduler.UnobservedTaskException` to log the full exception +
     stack + context (so #6 and any future crash land in the file). Consider keeping the app alive on a handled
     Dispatcher exception where safe, after logging.
   - **Settings toggle:** normal (Info+) vs **verbose/debug** (everything), **off by default**; verbose logs key actions
     — hotkey, show/hide, search, each insert/paste step, library scan/watch, import/export, template resolve, theme, errors.
   - **Easy retrieval:** "Open logs folder" (Settings + tray), optional "Copy diagnostics" (recent log + version + config).
   - **Constraint:** no NuGet (Serilog/NLog are the enterprise norm but are banned here) — hand-roll a small thread-safe
     `Logger` (BCL `StreamWriter` + lock/queue + size rollover). Keep it lean.
   </details>
8. **Richer tag UX (chips) + folder tags + batch-apply.** Three related pieces:
   - **Tag chips in the editor.** Replace the comma-separated **Tags** textbox (New/Edit response) with a chip/token
     input: type a tag + Enter/comma → it becomes a small **colored chip** (label + a little **×** to remove) that
     wraps under the input; add another and it appears alongside. Themed for light/dark. Storage unchanged (still the
     `tags` string array in the `.json` — no schema change). Nice touch: a stable color per tag (hash the name → a
     palette slot) so the same tag looks consistent everywhere; consider showing these chips on result rows / preview too.
   - **Tags on folders (categories).** Let a folder carry its own tags. Folders are plain directories today with no
     metadata — so this needs a **per-folder sidecar** (e.g. a small `.qrkfolder.json` in the directory) so folder tags
     **travel when the folder is shared/copied** (matches the app's "share by copying folders" model), or a central
     `config.json` map (local-only). **Recommend the sidecar**; must be back-compatible (folders without it just have
     no tags) and ignored by the response scanner. Set/edit via the nav folder's right-click menu.
   - **Batch-apply folder tags to responses.** An action on a folder — "Apply these tags to all responses in this
     folder" (ask direct-children vs. recursive) — that **unions** the folder's tags into each response's `tags`
     (deduped) and rewrites those files, **with a confirmation** showing how many will change. (Explicit batch-apply,
     not implicit inheritance, to avoid surprises — though implicit "inherit folder tags for search" could be a later
     option.) Search already matches tags, so batch-applied tags become searchable immediately.
9. **Robust, safe import — preview (diff) + hard-accept for code.** Two parts:
   - **Import preview.** Before importing someone else's folder/library, show a clear per-item preview of what will
     happen: **New** (added), **Changed** (same path, different content), **Duplicate/identical** (same content — skip),
     **Conflicting** (name clash needing a decision). Let the user review and choose before anything is written
     (apply-to-all for the safe cases; per-item where it matters). This extends today's import (which resolves
     conflicts + shows a summary, but doesn't pre-show the full picture). Compute the diff in `LibraryPorter`/
     `ImportCoordinator` by comparing incoming entries to the current library by relative path + content hash.
   - **Hard acceptance for code-bearing responses (security gate).** Pairs with the future **"sandboxed user scripting
     for variables"** idea (below): responses are shareable, so if a response ever carries **executable/custom code**
     (a scripted variable), importing it must **display the exact code** and require an explicit **per-item "I accept
     this code"** — **no "Yes to all" for code-bearing items**. Normal (code-free) responses keep the fast **Yes-to-all**
     path. This keeps importing untrusted libraries safe by default (a malicious template can never run code silently on
     import). Until user-code variables exist, only the **import-preview** part is buildable; keep this gate as a hard,
     non-optional requirement of that feature. (Today's built-in variables are a fixed safe allow-list with no code, so
     current imports carry no executable risk.)
10. **Automatic backups + Restore Library.** Before risky/bulk operations — imports (#9), batch tagging (#8), bulk
    move/delete, or major edits — snapshot the library (+ config) into a small **rolling backup** (keep last N,
    size/age-capped) under `%APPDATA%\QuickReplyKit\backups\` (a zip; reuse `LibraryPorter.ExportLibrary`). Add
    **Restore Library** (Settings/tray) to roll back to a chosen snapshot. High-value enterprise safety net — turns a
    botched bulk op into a one-click undo.
11. **In-app Trash + retention (soft delete).** Today delete already goes to the **Windows Recycle Bin** (so it's
    recoverable via Explorer), but an **app-local trash** makes recovery first-class: deleted responses/folders move to
    a local trash area, kept for a **configurable retention period**, with **Restore** and **Empty trash** in the app and
    auto-purge after the window. Makes "I deleted our whole IXL folder" an in-product undo, not a support ticket.
12. **First-run onboarding (≤3 screens).** A very short, skippable first-launch intro shown once (flag in config): the
    hotkey (**Ctrl+Alt+D**), where responses live, how to search, how to paste. Max ~3 screens. Cheap fix for the
    classic problem that users never discover half of what an app does.
13. **About / Diagnostics screen.** A small page: app **version**, library location, response count, registered
    hotkeys, watcher status, **log location** (#7), config location, and OS/runtime details — with buttons to open the
    logs/config/library folders. Complements the diagnostics logging (#7); ideal for support ("screenshot this page").
14. **Full accessibility pass.** Beyond resize + dark mode + the basic `AutomationProperties.Name` already added: full
    **keyboard control** (every action mouse-free), **screen-reader labels** on all controls incl. results/preview/editor,
    **high-contrast** theme compatibility, **reduced-motion** behavior (if animations are ever added), and **never using
    color alone** to convey status — pair the ⚠ invalid marker, favorite ★, and search-match highlight with a shape/label/text.

---

The list below is **out-of-current-scope future ideas only** — start any of these only if the user asks.

- **Sandboxed user scripting for variables.** Today's template variables are a **fixed, safe allow-list**
  (`{{greeting}}`, `{{date}}`, `{{time}}`, `{{day}}`, `{{date:<fmt>}}`) with **no code execution** — a deliberate
  security boundary, because responses are shareable files. A future "run custom code in a variable" feature would
  need a real sandbox / capability model (never raw eval), and careful UX around importing untrusted templates —
  it **requires** the code-import hard-acceptance gate in requested item #9 (show the code, accept each, no "Yes to all").
- **Usage analytics / most-used sort.** Optionally track per-response usage counts and offer a "most used" sort or
  view. Low value for a small library; only worthwhile if teams accumulate very large libraries.
- **In-app keyboard cheat-sheet.** A tiny "?" overlay listing the shortcuts (arrows/Enter/Shift+Enter/Ctrl+C, etc.).
  The shortcuts already work and are tooltipped; this is discoverability polish.

## Explicitly considered and declined (kept here so they aren't re-proposed)
- Natural/numeric name sort, number-to-pick (1–9) result selection — unnecessary for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, AI-generated responses — **out of scope
  by design** (see `PROJECT-BRIEF.md`); QuickReplyKit is a local, minimal launcher.
