# Phase 5 — Import / Export & Sharing

Responses are already shareable by copying `.json` files/folders into the library (the watcher picks
them up live). This phase adds the guided, portable path: zip export/import with conflict resolution.

## Core (`QuickReplyKit.Core.ImportExport.LibraryPorter`, headless + tested)
Use only `System.IO.Compression` (BCL — no NuGet).
- **Export single response** → copy the `.json` to a chosen path (`Export(item, destPath)`).
- **Export folder** → `ExportFolder(categoryPath, destZip)`: zip the folder subtree, preserving the
  relative hierarchy inside the zip.
- **Export whole library** → `ExportLibrary(destZip)`.
- **Import** → `Import(sourcePath, targetCategory, ConflictPolicy) -> ImportResult`:
  - Source may be a single `.json` or a `.zip` (of `.json` files in a folder hierarchy).
  - Each entry imported **independently**: invalid/unparseable entries are skipped and counted, never
    blocking valid ones (`ImportResult { Imported, SkippedInvalid, Conflicts, list of messages }`).
  - **Conflict** = a response already exists at the target relative path. `ConflictPolicy`:
    `Overwrite | KeepBoth (suffix "(imported)"/next free) | Skip`. Return per-file outcomes.
- Unit tests (temp dirs + real zips): round-trip export→import equals; hierarchy preserved; invalid
  entry in a zip doesn't abort the rest; each conflict policy behaves; importing a single `.json`.

## UI
- Menu (toolbar overflow "⋯" or a small menu button) + results context menu:
  - **Export response…**, **Export folder…** (on a nav category), **Export library…** → save dialog.
  - **Import…** → open dialog (accept `.json` or `.zip`) → choose target category → if conflicts,
    show a **conflict dialog** (list, per-item or apply-to-all choice: Overwrite / Keep both / Skip),
    then run and show a summary toast ("12 imported, 1 skipped, 2 kept both").
- After import, the watcher refreshes the library automatically.

## Validation (separate QA agent, ponytail for any code)
- Build clean, tests green (report count). Round-trip + conflict + invalid-tolerance covered.
- Screenshot the import conflict dialog and a summary. Manual-ish: export the IXL folder, delete it,
  re-import → identical hierarchy restored; import a zip containing one broken file → others still land.
