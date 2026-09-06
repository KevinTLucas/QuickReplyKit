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
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output is a single **`QuickReplyKit.exe`** (~66 MB, self-contained — no .NET install needed on the
target machine) with a `DefaultLibrary` folder beside it (the example content copied on first run).
Copy both to any Windows PC and run the exe. Trimming is intentionally not enabled (WPF is not
trim-safe).

## Managing responses

From the launcher you can, via the `+` / clipboard buttons and the right-click menu on a response:

- **New** — create a response and choose (or create) its folder in the hierarchy.
- **New from clipboard** — turn whatever you just copied into a new response, opened in the editor.
- **Edit** — a built-in rich-text editor (bold, italic, underline, bullet/numbered lists, links,
  paragraphs) — no need to touch markup or files.
- **Duplicate**, **Rename**, **Move to…** another folder.
- **Copy** to the clipboard, **Open containing folder** in Explorer.
- **Delete** — with confirmation (sent to the Recycle Bin).

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
- **Remember window** size and position (restored on-screen even if monitors change).
- **Launch at startup**.

## System tray

QuickReplyKit stays available in the system tray. The tray menu offers: **Open QuickReplyKit**,
**Open Response Library**, **Import… / Export library…**, **Settings**, and **Exit**.

## Where the library lives

By default: `Documents\QuickReplyKit\Library` (configurable in Settings). App settings and the recent
list live under `%APPDATA%\QuickReplyKit`. On first run the default example library is seeded so you
have something to try immediately.
