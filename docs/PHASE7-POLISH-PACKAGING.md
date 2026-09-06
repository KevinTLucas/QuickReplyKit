# Phase 7 — Polish, Packaging & Final Verification

The last phase: make it feel finished, prove the whole workflow, and produce a portable exe others
can run. Branch: `phase-7-polish`. Ponytail throughout; no NuGet.

## 1. Rich-paste end-to-end (fills the Phase-3 QA gap)
- Prove **rich (CF_HTML) paste** lands formatted, not just plain. WordPad is gone on Win11 24H2, so
  use one of: Word via COM/UIA if installed; a tiny Win32 **RichEdit** host in the self-test; or an
  in-process WPF `RichTextBox` that accepts CF_HTML. Add `--selftest-paste-rich` printing
  `RICHPASTE=<ok|fail>` and confirm.
- Also add a **live-UI** paste check (`--selftest-paste-ui` or scripted UIA): summon the launcher over
  a target, arrow to a response, press Enter, assert insertion + focus return + clipboard restore.
  (This is the path the top-priority bug lives on — keep it as a regression guard.)

## 2. Full-workflow regression (the MVP acceptance test)
Script/verify the headline loop end to end: focus another app → **global hotkey** → type a few chars →
arrow → Enter → response inserted at the caret → focus returns → original clipboard intact → our
content absent from clipboard history (document the manual Win+V spot-check). This is the definition of
"MVP complete."

## 3. Edge-case & UX sweep (screenshot + Read each)
Long response names (ellipsis + tooltip), deep folder nesting (scrolls), empty folder / empty library /
no-search-results states, invalid response files (⚠, not paste-able), a **large library** (generate
~2–5k responses → confirm search + scroll stay snappy, launcher opens instantly). Fix overlap/clipping
at min size and at 100/150/200% DPI. Address relevant `BACKLOG.md` items (esp. surfacing invalid files).

## 4. Packaging — portable exe
- `dotnet publish src/QuickReplyKit -c Release -r win-x64 --self-contained true` with
  `-p:PublishSingleFile=true` (and `-p:IncludeNativeLibrariesForSelfExtract=true`). WPF limits
  trimming — do **not** force `PublishTrimmed` unless verified. Confirm the produced `.exe` runs on a
  clean path, seeds the default library, registers the hotkey, and pastes.
- Ensure `assets/DefaultLibrary` ships alongside/inside the published output (the csproj already copies
  it to the build output; verify it survives publish).
- Document the output size and the run instructions.

## 5. Docs
- **README.md** (user-facing): what it is, install/run, the hotkey workflow, managing responses,
  sharing (copy folders / import-export), settings, tray. Screenshots.
- Update `STATUS.md` to mark the project MVP-complete and list any residual backlog.

## Validation gate / QA
- All self-tests green (`--selftest-paste`, `-rich`, `-ui`), full-workflow regression passes, published
  portable exe verified on a clean run, no overlap/clipping across sizes+DPI, large-library perf ok.
- Independent QA subagent signs off on the regression + packaging before final merge to `main`.
