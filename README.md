# QuickReplyKit

A lightweight Windows desktop **response launcher** for support teams. Press a global hotkey, find a
prewritten response, and insert it at your cursor in whatever app you were using — rich text or plain —
**without disturbing your clipboard** and **without leaving your content in Windows clipboard history**.

Responses are just a folder tree of `.json` files, so sharing is as simple as copying files or folders.

## The workflow (press Ctrl+Alt+D)

1. You're working in another app (browser, CRM, Outlook, Teams, …).
2. Press **Ctrl+Alt+D** — the launcher pops up instantly.
3. **Type a few characters** to search (by name, app, folder, tags, or body).
4. **Arrow up/down** to the response you want; the preview updates as you go.
5. Press **Enter** to insert it at your cursor. (**Shift+Enter** inserts as plain text; **Ctrl+C** copies.)
6. The launcher hides and focus returns to your app — your original clipboard is left intact.

Search checks response names, apps, folders, tags, and body text. Selecting a folder adds a removable filter chip and
searches only that folder and its descendants; selecting **All Responses** or removing the chip returns to global
search without clearing what you typed. Favorites and Recent search only those views. Minor misspellings are
considered only when a token has no exact match in the active view, keeping ordinary searches precise and fast.

The hotkey is configurable in Settings. Single-click selects and previews a response (it never
auto-inserts); double-click inserts.

## Build & run (from source)

Requires the **.NET 10 SDK** (Windows). WPF app, C#, no third-party packages.

```bash
# Build and test
dotnet build QuickReplyKit.slnx -c Debug
dotnet test  QuickReplyKit.slnx

# Run (starts in the system tray; --show pops the launcher immediately)
dotnet run --project src/QuickReplyKit -- --show
```

## Build a portable single-file exe

```bash
dotnet publish src/QuickReplyKit -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
```

Output is a single **`QuickReplyKit.exe`** (~67 MB, self-contained — no .NET install needed on the
target machine), plus an optional `DefaultLibrary` folder beside it. Trimming is intentionally not
enabled (WPF is not trim-safe); `-p:DebugType=none` keeps the folder clean (no `.pdb`).

**To distribute: give end users just `QuickReplyKit.exe`.** It runs on its own — copy that one file to
any Windows 10/11 PC and run it. The `DefaultLibrary` folder is optional: include it to seed a few
example responses on first run; omit it and the app simply starts with an empty library (users create
or import their own). Notes: the first launch self-extracts native libraries to a temp folder (a few
seconds, once), and Windows SmartScreen may prompt for an unsigned exe (**More info → Run anyway**, or
code-sign the exe for smoother distribution).

## Runtime requirements & dependencies (works on a clean PC with nothing installed)

The portable build is **self-contained**: the entire .NET runtime, WPF, and all native runtime libraries
are **bundled inside `QuickReplyKit.exe`** and extracted to a per-user temp folder on first run. A target
PC needs **only Windows** — no .NET, no Visual C++ Redistributable, no other packages.

**Requirement:** 64-bit **Windows 10 (1607+) or Windows 11**. (x64. Not for ARM/32-bit or older Windows.)

**Bundled inside the exe (you do NOT install these):** the full .NET 10 runtime + WPF (~250 framework
assemblies) and the native runtime — `coreclr.dll`, `clrjit.dll`, `hostfxr/hostpolicy.dll`,
**`vcruntime140_cor3.dll`** (its own C++ runtime — so no separate VC++ Redistributable is required),
`PresentationNative_cor3.dll`, `wpfgfx_cor3.dll`, `D3DCompiler_47_cor3.dll`, `System.IO.Compression.Native.dll`.

**External DLLs used (all are standard Windows system libraries, present on every Windows 10/11):**
`user32.dll`, `kernel32.dll`, `gdi32.dll` (windowing/input/graphics), `dwmapi.dll` (dark title bar),
`shell32/ole32` (Explorer + shell), plus WPF's OS-provided DirectX/DirectWrite. No third-party or
redistributable DLLs are required.

So: copy the one exe to any supported Windows PC and it runs — no prerequisites to install.

## Managing responses

From the launcher you can, via the `+` / clipboard buttons and the right-click menu on a response:

The active folder or built-in view is marked with a slim selection edge and emphasized label, so the current search
scope remains visible in Light, Dark, and Windows High Contrast themes. The search-scope chip uses the selected
folder's short display name while filtering with its complete nested path.

- **New** — create a response and choose (or create) its folder in the hierarchy.
- **New from clipboard** — turn whatever you just copied into a new response, opened in the editor. Clipboard HTML
  is reduced to QuickReplyKit's supported formatting; scripts, hidden/embedded content, and unsafe links are removed.
- **Edit** — a built-in rich-text editor: bold, italic, underline, **strikethrough**, bullet/numbered
  lists, links, paragraphs, plus **font size/heading, standard text colors, highlight colors, and
  alignment** — no need to touch markup or files. Tags are entered as removable colored chips.
- **Edit & insert** — open the selected response (with variables already resolved) in the editor, tweak
  the final message, and insert it — **without** saving a new response.
- **Duplicate**, **Rename**, **Move to…** another folder.
- **Copy** to the clipboard, **Open containing folder** in Explorer.
- **Instant-paste hotkey** — right-click a response to bind a global rich/plain paste shortcut.
- **Delete** — with confirmation, into QuickReplyKit's recoverable in-app Trash. Right-click a category to delete
  and later restore its complete folder tree, including nested responses and side files.

Older libraries may contain `.qrkfolder.json` files from a retired folder-tag feature. QuickReplyKit leaves these
legacy sidecars in place but ignores them; they never appear as responses or affect response tags.

## Templates: variables & reusable sections

Responses can contain `{{…}}` tokens that are filled in **when you insert** (and shown resolved in the preview):

- **Built-in variables:** `{{greeting}}` (Good morning/afternoon/evening by time of day), `{{date}}`,
  `{{time}}`, `{{day}}`, and `{{date:yyyy-MM-dd}}` (any .NET date format). Insert them from the editor's
  **`{{ }}`** menu.
- **Includes:** reference another response by name to reuse sections — e.g. a template of
  `{{Welcome}}` / body / `{{Closing}}` keeps greetings and sign-offs consistent everywhere. Cycles and
  missing references are shown as clear inline markers, never a hang.
- **Sandboxed scripts:** trusted local definitions can supply computed text with `{{script:Name}}`. The intentionally
  small `qrk-expr-1` language supports literals, clock values, comparisons, boolean logic, and bounded pure functions
  such as `if`, `concat`, `upper`, `lower`, `date`, and `time`. It cannot access files, processes, the network,
  environment, registry, clipboard, UI, reflection, or native code. Example: `if(hour < 12, "Good morning", "Hello")`.

Open **Settings → Scripts…** to create, validate, enable, review, export, or delete definitions. Trust is local and tied
to the exact source: editing a definition revokes trust, and QuickReplyKit never exports approval state.

## Sharing responses

- **Copy folders/files** directly into your library — the folder hierarchy and responses appear
  automatically (no restart; the app watches the library for changes).
- Use the launcher's labelled **⇄ Import / Export** menu to import responses or export the full library. Export one
  response from its result context menu, or export one folder from its folder context menu. Import first shows a
  selectable New/Changed/Duplicate/Identical/Invalid/Unsafe review list; changed paths can be skipped, overwritten,
  or kept as a second copy. Invalid content never blocks valid responses. Script definitions show their exact source,
  start unchecked, and require a separate explicit acceptance for every item; Select all never accepts code.

Broken/invalid `.json` files show up in **All Responses** marked with a ⚠ (muted, and not
paste-able) with the parse error in the preview, so you can find and fix them.

## Favorites & recent

- Tap the ★ on any response to favorite it; the **Favorites** view gives fast access.
- The **Recent** view lists responses you've recently inserted.

## Settings

Open from the ⚙ button or the tray. You can set:

- **Global hotkey** (default **Ctrl+Alt+D**).
- **Library folder** location.
- **Backup folder** location, with Change/Open. The default is `%USERPROFILE%\Documents\QuickReplyKit\Backups`;
  managed local or network paths are accepted when they are absolute, writable, and outside the response library.
- **Default paste behavior** (rich text or plain text).
- **Appearance** — System / Light / **Dark** theme (System follows your Windows setting).
- **Accessibility** — keyboard-first navigation, named controls and live status for screen readers, High Contrast
  compatible theme resources, keyboard-visible focus, readable active formatting states, and reduced popup motion.
- **Remember window** size and position (restored on-screen even if monitors change).
- **Launch at startup**.
- **Keyboard shortcuts…** — opens the shortcuts manager (see below).
- **Scripts…** — opens the sandboxed-script manager for create/edit/validate/trust/enable/export/delete workflows.
- **Back up / Restore Library…** — create or restore one of the 10 rolling library-and-settings snapshots.
- **Trash…** — restore deleted responses or folders, remove one permanently, or empty Trash. Deleted items
  are kept for 30 days by default.
- **Diagnostics** — enable verbose logging, open the rolling logs folder, or open About / Diagnostics.

## Per-response instant-paste hotkeys

Bind a global hotkey to a **specific** response and it pastes straight into whatever app you're in — **without
opening QuickReplyKit**. Right-click a response → **Instant-paste hotkey…**, press a combination, and choose rich
or plain text. These binds are stored locally (in your `config.json`, keyed by the response's path), so a shared
library never fights over global keys.

Open **Settings → Keyboard shortcuts…** for the manager: it lists the launcher hotkey and every per-response bind,
flags conflicts (a duplicate within QuickReplyKit, or a combo another app already owns), and provides a text filter
over shortcut combinations and response names. If a hotkey can't be registered
(already in use), the app keeps running and the manager shows it as "in use by another app".

## Backups, Trash, and diagnostics

- QuickReplyKit automatically creates a required rolling backup before an import; if that safety copy cannot be
  written, the import is canceled before it changes anything. You can create or restore a snapshot from
  **Settings → Back up / Restore Library…**. Current snapshots contain the response library and app settings;
  older library-only snapshots still work. Restore protects the selected archive, snapshots the current state, and
  validates and swaps the staged library into place with rollback on failure. Restored settings keep the current
  library location, and QuickReplyKit restarts after applying them. Backup extraction is bounded against oversized ZIPs.
  Every manual and required snapshot uses the configured backup folder. If that folder is offline or read-only,
  QuickReplyKit starts normally but clearly refuses the affected backup, restore, import, or folder-delete operation.
- Deleted responses and category folders move to **Settings → Trash…**, where each item is labeled by type and can
  be restored to its exact original path unless a file or folder now occupies that location. Folder deletion first
  creates a required rolling backup and fails without changing the library if that backup cannot be written.
  Permanent delete and Empty Trash require confirmation; retention applies to both response and folder items.
- Diagnostic logs live under `%APPDATA%\QuickReplyKit\logs`, roll at about 50 MB total, and record structured workflow
  stages, counts, timings, modes, and safe crash frames. They exclude search text, response names/tags/content,
  template token names, clipboard data, hotkey values, window handles, user paths, and exception messages. Enable
  verbose detail only while troubleshooting. **About / Diagnostics…** provides a copyable support summary with
  version, OS/runtime, aggregate hotkey-registration counts, library/count/watcher state, actual logging status, and
  one-click access to the library, config, logs, backups, and Trash. The copied report shows the resolved filesystem
  paths needed for troubleshooting while excluding response details, individual response hotkeys, and
  clipboard/customer content.
- First launch shows a short three-card introduction to the hotkey, search, and paste workflow.

## Current limitations before broad rollout

- Import preview classifies each response as New, Duplicate elsewhere, Changed, Identical, Invalid, or Unsafe. Check
  only the responses you want, use Select all/Clear for ordinary files, and choose Overwrite or the safer Keep both
  option per selected conflict. Identical, invalid, and unsafe files stay disabled. Select all never accepts
  code-bearing definitions; those display exact source and require individual review and acceptance. The accepted
  preview is the exact plan that executes, and files changed after preview are reported stale rather than overwritten.
- Legacy folder-tag sidecars are ignored during import and never appear as responses.

See [`docs/STATUS.md`](docs/STATUS.md) for the audited state and [`docs/BACKLOG.md`](docs/BACKLOG.md) for remaining work.

## System tray

QuickReplyKit stays available in the system tray. The tray menu offers: **Open QuickReplyKit**,
**Open Response Library**, **Import… / Export library…**, **Settings**, **Open logs folder**, and **Exit**.

## Where the library lives

By default: `%USERPROFILE%\Documents\QuickReplyKit\Library` (configurable in Settings). Backups default to
`%USERPROFILE%\Documents\QuickReplyKit\Backups` and are separately configurable. App settings, recent items, logs,
Trash, and script trust live under `%APPDATA%\QuickReplyKit`.
When the optional `DefaultLibrary`
folder is distributed beside the executable, its examples seed an empty library on first run; a
single-exe distribution starts with an empty library instead.

## License

QuickReplyKit is available under the [MIT License](LICENSE).
