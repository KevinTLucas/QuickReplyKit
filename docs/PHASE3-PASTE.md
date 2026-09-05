# Phase 3 — Paste Engine (hotkey → focus restore → insert → clipboard-safe)

This is the core value of QuickReplyKit and the riskiest interop. Get correctness first, then speed.

## The full insert sequence
Triggered when the user activates a response (Enter / button / double-click):
1. **Snapshot** the user's current clipboard (so we can restore it).
2. **Hide** the launcher window.
3. **Restore foreground** to the target window captured when the launcher opened.
4. **Write** the response onto the clipboard **with history-exclusion** (raw Win32).
5. **SendInput** Ctrl+V to the target.
6. **Wait** briefly (~120 ms, configurable) for the target to consume the paste.
7. **Restore** the snapshotted clipboard.

Net effect: text lands at the caret in the other app; the user's clipboard is unchanged afterward;
our content never enters Windows Clipboard History / Cloud Clipboard.

## Components (`QuickReplyKit` app project, `Interop/` + `Services/`)

### GlobalHotkey (`Interop/GlobalHotkey.cs`)
- Message-only `HwndSource` (invisible) + `RegisterHotKey`. Parse the gesture string
  (`AppConfig.HotkeyGesture`, e.g. `"Ctrl+Alt+Space"`) → `MOD_*` flags + virtual-key.
- On `WM_HOTKEY`: **capture `GetForegroundWindow()` FIRST** (that's the target), THEN raise an
  event the App handles to `ShowLauncher()`. Order matters — capture before we steal focus.
- `Rebind(gesture)` for settings changes. If `RegisterHotKey` fails (already taken), do NOT crash:
  surface a tray balloon / settings warning and keep running so the user can pick another.

### ForegroundTarget (`Interop/ForegroundWindow.cs`)
- `GetForegroundWindow`, `SetForegroundWindow`, plus a reliable focus-restore helper that works
  around Windows' foreground lock: try `SetForegroundWindow`; if `GetForegroundWindow()!=target`,
  fall back to `AttachThreadInput(ourThread, targetThread, true)` → `SetForegroundWindow` →
  `AttachThreadInput(..., false)`, and/or a `ShowWindow(SW_SHOW)`/`BringWindowToTop`. Poll up to a
  few tries with tiny sleeps until foreground == target (or give up after ~200 ms and paste anyway).

### ClipboardWriter (`Interop/ClipboardWriter.cs`) — the history-exclusion write
- Raw Win32: `OpenClipboard` (retry a few times; it can be briefly locked), `EmptyClipboard`,
  then `SetClipboardData` for each format with `GlobalAlloc(GMEM_MOVEABLE)` + `GlobalLock` copy:
  - `CF_UNICODETEXT` (plain text) — always.
  - `RegisterClipboardFormat("HTML Format")` — CF_HTML from `CfHtml.Build(item.Html)` (rich only).
  - Exclusion (register each, put a tiny/zero payload):
    - `"ExcludeClipboardContentFromMonitorProcessing"`
    - `"CanIncludeInClipboardHistory"` → DWORD `0`
    - `"CanUploadToCloudClipboard"` → DWORD `0`
  - `CloseClipboard`. Always free/handle on failure paths; never leave the clipboard open.
- CF_HTML bytes are **UTF-8**; CF_UNICODETEXT is UTF-16LE null-terminated.

### ClipboardBackup (`Interop/ClipboardBackup.cs`)
- `Snapshot()`: read present formats via WPF `Clipboard.GetDataObject()` and copy Text, UnicodeText,
  HTML, RTF, Bitmap/Image, FileDropList into a detached `DataObject`. Tolerate an empty/locked clipboard.
- `Restore(snapshot)`: `Clipboard.SetDataObject(snapshot, copy:true)`, retrying on transient lock.
  If snapshot was empty, clear our content (set an empty text) so we don't leave the response behind.

### InputSender (`Interop/InputSender.cs`)
- `SendCtrlV()` via `SendInput`: Ctrl down, V down, V up, Ctrl up (use `KEYEVENTF_*`). Ensure no
  modifier is left stuck. (User has released the hotkey keys by insert time.)

### InsertService (`Services/InsertService.cs`) — implements the Phase 2 `IInsertService`
- `InsertRich(item)`: sequence above with CF_HTML + text.
- `InsertPlain(item)`: sequence above with text only (still history-excluded, still restore).
- `Copy(item)`: **user-intended** copy — set clipboard normally (WPF `Clipboard.SetText`/HTML), NO
  exclusion, NO restore (they want it in the clipboard to paste later). Then just hide.
- After a successful insert: `LibraryService.MarkUsed(item)` (recent) and hide the launcher so focus
  is already back on the target. Guard against double-insert (disable during the ~300 ms sequence).

## Reliability notes
- Do the sequence on the UI thread but keep the waits tiny; the perceived latency is only steps 3–5.
- If the target window vanished (closed) between open and insert, fall back to just leaving the text
  on the clipboard (no exclusion) and show a subtle hint "Copied — original app was closed".
- Must **not** install any mouse/keyboard hook (only `RegisterHotKey`), so browser/CRM right-click
  menus are never affected.

## Validation (separate QA agent)
- Unit: gesture parser (`"Ctrl+Alt+Space"`, `"Alt+Shift+Q"`, invalid strings → sensible failure),
  CF_HTML already covered.
- **E2E self-test** — add a hidden entrypoint `--selftest-paste` that: sets a sentinel clipboard,
  launches Notepad, focuses it, captures its hwnd as target, runs `InsertPlain` with a known string,
  reads Notepad's text back (WM_GETTEXT or UI Automation), and prints:
  `PASTE=<ok/fail> CLIPBOARD_RESTORED=<ok/fail>`. QA runs it and confirms both `ok`. Also a rich
  variant into WordPad/Word if available (optional; plain into Notepad is the must-pass gate).
- Manual-ish: with the app running, real hotkey over Notepad inserts at the caret and returns focus.
- Confirm our pasted content does NOT appear in Win+V history (spot check / documented manual step).
