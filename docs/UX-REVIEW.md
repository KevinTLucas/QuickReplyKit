# QuickReplyKit — End-user UX review & continuous-improvement backlog

From a realistic end-user evaluation (2026-09-06) against a ~4,800-response library, both themes,
compact/large sizes, live keyboard, plus engine benchmarks. Overall: a well-built, focused utility;
the one real weakness is large-library startup performance. Items are being worked in value order.

## Doing now (continuous-improvement batches)
- **CI-1 · Performance (highest value): ✅ DONE — merged to `main` (`864db1a`).**
  - **B1** — startup scanned the whole library **synchronously on the UI thread** (~19s to show with ~4.8k
    responses). Fixed: `LibraryScanner.Scan` now loads files with `Parallel.For` (I/O-bound; deterministic
    reassembly keeps the snapshot identical), **and** the initial scan runs on a background `Task` — the
    launcher shows immediately with a "Loading library…" state and populates via the existing watcher path.
    Local bench (~4.8k files, warm cache): ~2.9× faster (181 ms vs 532 ms); larger on real AV/OneDrive hardware.
  - **H1** — `ResponseFile.Load` called `File.GetCreationTimeUtc` + `GetLastWriteTimeUtc` per file for the
    common shared/copied-file case; `Created`/`Modified` are **never displayed or used for sort/filter**.
    Fixed: they stay `default` when absent (no syscall; `Save` still fills `Created`). ~19% of the scan here.
- **CI-2 · Search quality: ✅ DONE — merged to `main` (`4d8cee3`).**
  - **H2** — results don't show *why*/where a query matched (body/tag hits show a name without the term).
    Bold the matched substring in the name; show a matching-line snippet when the hit is body-only.
  - **M1** — no typo tolerance: "licence" → 0 results. Add a **lazy, dependency-free** fallback (subsequence
    / edit-distance ≤1 on name+tags at a low tier) only when a token has zero exact matches.
  - **M2** — typing a query silently ignores the selected folder. Rank in-scope (`CategoryPath` matches
    selected nav) hits first (keep global search, just prioritize scope).
  - **M4** — search filters to valid-only, so a broken response can't be found to fix it. Let search match
    invalid files by **name** (still non-pasteable).
- **CI-3 · Interaction polish: ✅ DONE — merged to `main` (`53a1333`).**
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
