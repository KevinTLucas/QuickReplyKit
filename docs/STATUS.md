# QuickReplyKit — Project Status (living handoff)

> **Single source of truth for "where we are."** A fresh session or a different AI should be able to read
> [`../CLAUDE.md`](../CLAUDE.md) + this file and continue with zero prior context.
> **Update this file on every meaningful change** — it's part of the Definition of Done (see CLAUDE.md).

- **Last updated:** 2026-09-06
- **`main` tip:** `8f74aa9` · **`dotnet test` = 407 passing** · `dotnet build` clean (0 warnings) · new app icon shipped.
- **App: ✅ feature-complete & merged** — MVP + all V2 features + the end-user continuous-improvement pass are done.
  Polished, launchable, and portable.
- **⏳ Backlog sprint in progress** (2026-09-06): working through [`BACKLOG.md`](BACKLOG.md) top-down.
  **Merged to main:** BUG #6 (right-click crash), BUG #1 (dark ComboBox), #7 diagnostics/logging.
  **Awaiting QA merge:** #3 Edit button + #2 Automatic colour (`feature/edit-palettes`), #13 About screen
  (`feature/about-screen`), #10 backups + restore (`feature/backups`). **Not yet started:** #4/#5 hotkeys, #8 tags,
  #9 import preview, #11 trash, #12 onboarding, #14 accessibility.
- **Both known bugs are now fixed** — no open bugs. Remaining work = the queued *features* in BACKLOG.md.

## What it is / what it does
A lightweight Windows response launcher for support teams: global hotkey (**Ctrl+Alt+D**) → search/browse a
folder-tree library of prewritten responses → preview → insert at the cursor in whatever app you were in,
**preserving your clipboard** and **out of Windows clipboard history**. Requirements: [`PROJECT-BRIEF.md`](PROJECT-BRIEF.md).
Design + code map: [`../ARCHITECTURE.md`](../ARCHITECTURE.md). User guide: [`../README.md`](../README.md).

## Feature set (all shipped)
- **Launcher** — 3-pane nav/results/preview, live search (name/app/folder/tags/body) with **match highlighting +
  body snippets + typo tolerance + in-scope ranking**, keyboard-first (type→arrows→Enter; Shift+Enter plain; Ctrl+C copy),
  favorites, recent, resizable/DPI-aware, compact.
- **Paste engine** — global hotkey, reliable target-window tracking (`ForegroundMonitor`), clipboard-safe insert with
  history-exclusion; rich + plain; Copy with "Copied ✓" feedback.
- **Editor & management** — rich-text editor (B/I/U, **strikethrough, font size/heading, color, highlight, alignment**,
  lists, links) + create/edit/duplicate/rename/move/delete-to-Recycle-Bin, new-from-clipboard, **edit-before-insert**,
  open-in-Explorer.
- **Templates** — built-in variables (`{{greeting}}`/`{{date}}`/`{{time}}`/`{{day}}`/`{{date:fmt}}`) + response
  **includes** `{{Name}}` (cycle-safe; **no code execution**).
- **Import/export** (zip, conflict resolution, invalid-tolerant, path-traversal-safe), **settings** (hotkey, library
  path, default paste, **theme**, remember-window, launch-at-startup — persisted), **system tray**, **dark/light theme**
  (System-follow), window memory + multi-monitor, **fast startup** on large libraries (parallel, off-UI-thread scan),
  invalid-file surfacing (⚠), portable single-file packaging.

## Known caveats (non-blocking, documented on purpose)
- ~~BUG #6 right-click crash~~ **FIXED** (merged `d562052`): context menus now gate in `ContextMenuOpening` via
  `e.Handled` instead of mutating `IsOpen` inside `Opened`. QA: survived 80 rapid right-clicks.
- ~~BUG #1 dark-mode ComboBox~~ **FIXED** (merged `d562052`): app-wide themed `ComboBox`/`ComboBoxItem` template in
  `App.xaml` (DynamicResource brushes). QA: readable in both themes, closed + open dropdown.
- A **highlight** color with no explicit text color can be low-contrast in dark mode (same as Word/Outlook). The editor's
  new **"Automatic (theme default)"** text-colour option lets a user revert to theme-inherited (readable) text.
- `--selftest-paste-rich` proves the clipboard carries valid **CF_HTML** (what Outlook/Word/browsers render), not an
  in-app render (Win11 Msftedit has no HTML importer).
- **2 human spot-checks** (un-automatable): a live **Ctrl+Alt+D** paste into a third-party app; confirm pasted text is
  absent from the **Win+V** history flyout.

## Verify / extend in a fresh session
1. Read [`../CLAUDE.md`](../CLAUDE.md) (how to work, conventions, git flow, Definition of Done), then this file and
   [`../ARCHITECTURE.md`](../ARCHITECTURE.md).
2. `export PATH="/c/Program Files/dotnet:$PATH"; dotnet build QuickReplyKit.slnx && dotnet test QuickReplyKit.slnx`
   → expect **403** green. Paste engine: `QuickReplyKit.exe --selftest-paste` → `PASTE=ok CLIPBOARD_RESTORED=ok`.
3. For new work: branch off `main` (worktree), implement with **ponytail**, validate, **independent-QA via a separate
   subagent** (build + tests + both-theme screenshots + the 4 self-tests + back-compat), merge `--no-ff`, **update this file**.
