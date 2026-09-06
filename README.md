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

- **New** — create a response and choose (or create) its folder in the hierarchy.
- **New from clipboard** — turn whatever you just copied into a new response, opened in the editor.
- **Edit** — a built-in rich-text editor: bold, italic, underline, **strikethrough**, bullet/numbered
  lists, links, paragraphs, plus **font size/heading, text color, highlight, and alignment** — no need
  to touch markup or files.
- **Edit & insert** — open the selected response (with variables already resolved) in the editor, tweak
  the final message, and insert it — **without** saving a new response.
- **Duplicate**, **Rename**, **Move to…** another folder.
- **Copy** to the clipboard, **Open containing folder** in Explorer.
- **Delete** — with confirmation (sent to the Recycle Bin).

## Templates: variables & reusable sections

Responses can contain `{{…}}` tokens that are filled in **when you insert** (and shown resolved in the preview):

- **Built-in variables:** `{{greeting}}` (Good morning/afternoon/evening by time of day), `{{date}}`,
  `{{time}}`, `{{day}}`, and `{{date:yyyy-MM-dd}}` (any .NET date format). Insert them from the editor's
  **`{{ }}`** menu.
- **Includes:** reference another response by name to reuse sections — e.g. a template of
  `{{Welcome}}` / body / `{{Closing}}` keeps greetings and sign-offs consistent everywhere. Cycles and
  missing references are shown as clear inline markers, never a hang.

(Variables are a fixed, safe built-in set — no code is ever executed, so shared templates are safe to import.)

## Sharing responses

- **Copy folders/files** directly into your library — the folder hierarchy and responses appear
  automatically (no restart; the app watches the library for changes).
- Or use **Import… / Export…** (single response, a folder, or the whole library as a `.zip`). On a
  conflict, import lets you choose what to keep; invalid content never blocks the valid responses.

Broken/invalid `.json` files show up in **All Responses** marked with a ⚠ (muted, and not
paste-able) with the parse error in the preview, so you can find and fix them.

## Favorites & recent

- Tap the ★ on any response to favorite it; the **Favorites** view gives fast access.
- The **Recent** view lists responses you've recently inserted.

## Settings

Open from the ⚙ button or the tray. You can set:

- **Global hotkey** (default **Ctrl+Alt+D**).
- **Library folder** location.
- **Default paste behavior** (rich text or plain text).
- **Appearance** — System / Light / **Dark** theme (System follows your Windows setting).
- **Remember window** size and position (restored on-screen even if monitors change).
- **Launch at startup**.

## System tray

QuickReplyKit stays available in the system tray. The tray menu offers: **Open QuickReplyKit**,
**Open Response Library**, **Import… / Export library…**, **Settings**, and **Exit**.

## Where the library lives

By default: `Documents\QuickReplyKit\Library` (configurable in Settings). App settings and the recent
list live under `%APPDATA%\QuickReplyKit`. On first run the default example library is seeded so you
have something to try immediately.
