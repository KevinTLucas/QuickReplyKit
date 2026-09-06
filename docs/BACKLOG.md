# Backlog — future ideas

Everything previously tracked here is **done and shipped** (the MVP robustness items and all V2 features —
richer editor, edit-before-insert, template variables, response includes, dark mode — plus the end-user
continuous-improvement pass). See [`STATUS.md`](STATUS.md) for the current state and known cosmetic caveats.

## Requested — queued to build (user, 2026-09-06)

Prioritized; a bug first. Follow the normal flow (branch → ponytail → separate-subagent QA → merge; update docs).

1. **BUG · Dark-mode Category combobox is unreadable.** In the New/Edit response editor, the **Category (folder)**
   combobox's closed/editable field renders **white background with white text** in dark mode (can't read the typed
   path); the open dropdown is dark but its folder items are **near-black text on dark = very low contrast**. This
   supersedes the old "cosmetic combobox chrome" caveat — it's a real usability defect. **Fix:** give the editable
   `ComboBox` a proper themed `ControlTemplate` (or restyle its editable `TextBox` part + `ComboBoxItem`s) so the
   field background/foreground and the dropdown item text all use the theme brushes (`CardBg`/`TextPrimary`/`Accent`
   selection) with good contrast in **both** light and dark. Same treatment anywhere else an editable combobox is used.
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
6. **BUG (crash, HIGH) · Fast/repeated right-click on a result crashes the app.** Spamming right-click on a response
   (re-opening the context menu quickly) crashes QuickReplyKit. **Likely cause:** `LauncherWindow.ResultsContextMenu_Opened`
   sets `ResultsContextMenu.IsOpen = false` **inside the `Opened` handler** to suppress the menu when nothing is
   selected — mutating `IsOpen` during `Opened`, combined with `ResultsList_PreviewMouseRightButtonDown` re-selecting a
   row on every click, is a WPF reentrancy footgun that can throw (or `SelectedItem` is null mid-handler). **Fix:** stop
   toggling `IsOpen` in `Opened` — instead gate the menu in `ContextMenuOpening` (set `e.Handled = true` to cancel it
   when there's no valid selection) or via `IsEnabled`, null-guard the handler, and add a stress/repeat test. (Exactly
   the kind of crash the logging system #7 should capture automatically.)
7. **Diagnostics / logging system.** So "send me the log file" instantly gives what's needed to debug. Requirements:
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

---

The list below is **out-of-current-scope future ideas only** — start any of these only if the user asks.

- **Sandboxed user scripting for variables.** Today's template variables are a **fixed, safe allow-list**
  (`{{greeting}}`, `{{date}}`, `{{time}}`, `{{day}}`, `{{date:<fmt>}}`) with **no code execution** — a deliberate
  security boundary, because responses are shareable files. A future "run custom code in a variable" feature would
  need a real sandbox / capability model (never raw eval), and careful UX around importing untrusted templates.
- **Usage analytics / most-used sort.** Optionally track per-response usage counts and offer a "most used" sort or
  view. Low value for a small library; only worthwhile if teams accumulate very large libraries.
- **In-app keyboard cheat-sheet.** A tiny "?" overlay listing the shortcuts (arrows/Enter/Shift+Enter/Ctrl+C, etc.).
  The shortcuts already work and are tooltipped; this is discoverability polish.

## Explicitly considered and declined (kept here so they aren't re-proposed)
- Natural/numeric name sort, number-to-pick (1–9) result selection — unnecessary for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, AI-generated responses — **out of scope
  by design** (see `PROJECT-BRIEF.md`); QuickReplyKit is a local, minimal launcher.
