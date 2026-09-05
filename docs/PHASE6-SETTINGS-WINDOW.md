# Phase 6 — Settings, Window behaviour, Tray polish

Favorites & Recent already work (Phase 2). This phase completes user preferences, window memory,
and multi-monitor/DPI robustness.

## Settings window (`Views/SettingsWindow.xaml`)
Simple, single dialog persisting to `AppConfig` (Save/Cancel):
- **Global hotkey**: a capture control — focus it, press a combo, it records the gesture string
  (e.g. `Ctrl+Alt+Space`); validates it's a sane combo (modifier + key). On save, `GlobalHotkey.Rebind`
  the new gesture; if it fails to register, show inline "that shortcut is in use, pick another".
- **Library path**: show current, "Change…" folder picker (moving/using an existing folder), "Open in
  Explorer". Changing it re-points `LibraryService` (rescan + rewatch).
- **Default paste behaviour**: Rich text / Plain text (drives Enter).
- **Remember window size & position**: on/off.
- **Launch at Windows startup**: on/off → HKCU `...\Run` registry value (BCL `Microsoft.Win32`), optional.

## Window memory & multi-monitor (`WindowBounds` in AppConfig)
- Save size+position (and monitor device name) when the launcher hides/closes; restore on show.
- **Always reopen visible**: before showing, validate the saved rect intersects a current working
  area; if the monitor is gone or it's off-screen, re-center on the nearest/primary monitor's working
  area. Never restore off-screen. Use `System.Windows.Forms.Screen`/WPF `SystemParameters` (BCL).
- Handle DPI: already Per-Monitor v2 — verify the launcher is crisp and correctly sized at 100/150/200%
  and when dragged between monitors of different scale (screenshot at 150% and 200%).

## Tray
- Wire tray **Settings** → open `SettingsWindow`. Confirm Open / Open Response Library / Exit all work.
- Tray balloon for hotkey-registration failure (from Phase 3) points here.

## Validation (separate QA agent, ponytail for any code)
- Build clean, tests green (AppConfig persistence incl. WindowBounds + new fields; off-screen-rect
  correction is unit-testable with fake screen rects — extract the "clamp rect to visible area" as a
  pure function in Core and test it). Report count.
- Screenshot the Settings window; verify hotkey capture records a combo; verify restoring a saved
  off-screen position re-centers. Confirm preferences persist across an app restart.
