# QuickReplyKit — End-user UX review & continuous-improvement backlog

From a realistic end-user evaluation (2026-09-06) against a ~4,800-response library, both themes,
compact/large sizes, live keyboard, plus engine benchmarks. Overall: a well-built, focused utility;
the one real weakness is large-library startup performance. Items are being worked in value order.

## Doing now (continuous-improvement batches)
- **CI-1 · Performance (highest value):**
  - **B1** — startup scans the whole library **synchronously on the UI thread** (~19s to show with ~4.8k
    responses). Fix: **parallelize** `LibraryScanner` file loads (measured 3.6× faster; work is I/O-bound
    via AV/OneDrive filter drivers) **and** don't block the UI — show the launcher immediately with a brief
    "Loading library…" state, populate on a background task (watcher already marshals updates). Also makes
    `LibraryService.Reload()` (fires on every debounced change) cheap.
  - **H1** — `ResponseFile.Load` calls `File.GetCreationTimeUtc` + `GetLastWriteTimeUtc` per file for the
    common shared/copied-file case (~5.7s of the 18s); `Created`/`Modified` are **never displayed or used
    for sort/filter**. Drop the eager fallback (lazy/remove). ~30% off every scan. Pure ponytail win.
- **CI-2 · Search quality:**
  - **H2** — results don't show *why*/where a query matched (body/tag hits show a name without the term).
    Bold the matched substring in the name; show a matching-line snippet when the hit is body-only.
  - **M1** — no typo tolerance: "licence" → 0 results. Add a **lazy, dependency-free** fallback (subsequence
    / edit-distance ≤1 on name+tags at a low tier) only when a token has zero exact matches.
  - **M2** — typing a query silently ignores the selected folder. Rank in-scope (`CategoryPath` matches
    selected nav) hits first (keep global search, just prioritize scope).
  - **M4** — search filters to valid-only, so a broken response can't be found to fix it. Let search match
    invalid files by **name** (still non-pasteable).
- **CI-3 · Interaction polish:**
  - **M3** — "Copy" gives no feedback (looks inert). Add a brief "Copied ✓" on the button/meta line.
  - **L3** — icon-only buttons (☰ ＋ 📋 ⋯ ⚙) lack `AutomationProperties.Name` (screen-reader polish).
  - **L5** — guard `LibraryWatcher.Start`'s `Directory.CreateDirectory` so an offline OneDrive library path
    isn't recreated as a stray local folder.

## Considered, not doing (scope/again value)
- **L1** natural sort (rarely matters for named responses), **L2** number-to-pick (unnecessary), a keyboard
  cheatsheet (nice-to-have; maybe a tiny hint), **L4** dark combobox chrome (cosmetic, already in STATUS).

## Already good (no work needed)
Resize/DPI/compact (nav auto-collapse, splitters, min-widths); invalid-file handling; import/export
(conflict + invalid-skip + path-traversal-safe); editor (formatting, spellcheck, variables, edit-&-insert);
watcher debounce; settings/tray/single-instance/multi-monitor placement; search speed once loaded (2–15ms).
