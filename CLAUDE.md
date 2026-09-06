# QuickReplyKit — AI / Developer Operating Manual

> **New session or new AI? Read this file, then [`docs/STATUS.md`](docs/STATUS.md).**
> `CLAUDE.md` tells you **how** to work on this repo. `docs/STATUS.md` tells you **where we are
> right now** (current phase, in-progress work, known bugs, what to do next). Between the two you
> can pick up cold with zero prior context.

## What this is
QuickReplyKit is a lightweight **Windows desktop "response launcher"** for support teams: press a
global hotkey → find a prewritten response → insert it at the cursor in whatever app you were using
(rich text or plain), **preserving your clipboard** and **keeping our content out of Windows
clipboard history**. Responses are a folder tree of `.json` files; sharing = copying files/folders.

Full product requirements are the original brief; the design that implements them is
[`ARCHITECTURE.md`](ARCHITECTURE.md).

## Stack & toolchain
- **WPF, C#, .NET 10** (`net10.0-windows`). Solution: `QuickReplyKit.slnx` (new XML solution format).
- **`dotnet` is NOT on PATH by default.** It lives at `C:\Program Files\dotnet`. In every shell:
  ```bash
  export PATH="/c/Program Files/dotnet:$PATH"     # Git Bash
  ```
- Projects:
  - `src/QuickReplyKit.Core` — **all headless logic** (model, scanner, watcher, search, HTML/CF_HTML,
    storage, import/export, response management, converters). No UI. Fully unit-tested.
  - `src/QuickReplyKit` — the **WPF app** (Views, ViewModels [MVVM-lite], Interop [Win32/paste],
    Services, App wiring, tray, hotkey).
  - `src/QuickReplyKit.Tests` — **xUnit** tests over Core and the view-models.

## Build / test / run / verify (canonical commands)
```bash
export PATH="/c/Program Files/dotnet:$PATH"
dotnet build QuickReplyKit.slnx -c Debug          # must be clean, 0 warnings
dotnet test  QuickReplyKit.slnx --nologo          # must be fully green
# Run the app (opens to the system tray; use --show to pop the launcher immediately):
dotnet run --project src/QuickReplyKit -- --show
# Paste engine end-to-end self-test (launches Notepad, pastes, checks clipboard restore):
src/QuickReplyKit/bin/Debug/net10.0-windows/QuickReplyKit.exe --selftest-paste
#   → must print:  PASTE=ok CLIPBOARD_RESTORED=ok
```
**UI validation (WPF isn't browser-inspectable):** use the PowerShell helpers in `tools/`:
- `tools/shoot.ps1 -Out <png> [-AppArgs "--show --size 780x520"] [-Wait 4]` — launch, screenshot the
  window, kill it. Then **Read the PNG** to inspect. (`capture-window.ps1` = raw capture;
  `make-icon.ps1` = regenerate `app.ico`.) Screenshots go under the session scratchpad, never the repo.
- Always kill leftovers when done: `Get-Process QuickReplyKit,notepad | Stop-Process -Force`.

## Conventions & guardrails (do not violate without a note in STATUS.md)
- **No third-party packages.** BCL / native WPF only — no NuGet for the app or runtime. (Win32 via
  hand-rolled P/Invoke; zips via `System.IO.Compression`; JSON via `System.Text.Json`; tray via
  WinForms `NotifyIcon`; recycle-bin delete via `Microsoft.VisualBasic.FileIO`.) This keeps the
  portable exe lean.
- **Ponytail everything.** Write the shortest correct code; prefer BCL/native over custom; no
  speculative abstractions. Use the `ponytail` skill for writing and `ponytail-review` for review.
- **MVVM-lite**, hand-rolled: `ViewModelBase : INotifyPropertyChanged` + `RelayCommand`. No framework.
- **Storage schema is a contract** (see ARCHITECTURE.md): one `.json` per response, **filename = name**,
  content is a restricted HTML subset (bold/italic/underline/ul/ol/li/link/paragraphs). Don't break it
  — the seeded `assets/DefaultLibrary` and shared files must keep loading.
- **DPI:** Per-Monitor-v2 via `app.manifest`. Keep it.

## How we work (process)
- **Phased delivery.** Work proceeds in numbered phases (see STATUS.md). Each phase: implement →
  validate (build/test/screenshot/self-test) → **independent QA** → fix → merge.
- **Subagents.** Implementation is done by a subagent; **QA/validation by a SEPARATE subagent**
  (independence catches the implementer's blind spots). Parallelize independent work with subagents —
  use **git worktree isolation** for agents that would otherwise collide on `bin/obj` or files.
- **Git workflow (strict):**
  - `main` is **always green and launchable.** Never commit half-done work to `main`.
  - Every phase/feature is a branch: `phase-N-name` (e.g. `phase-3-paste`) or `feature/x`.
  - Merge to `main` with `--no-ff` **only after** build + tests + QA are green. Delete the branch after.
  - Commit messages end with: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
  - `.claude/` (agent worktrees/session data), `bin/`, `obj/`, `*.png`, `publish/` are git-ignored.
- **Keep the handoff current:** at the end of any meaningful work, **update `docs/STATUS.md`** (phase
  status, what changed, known issues, next actions). This is what makes cold pickup possible.

## Document map
- [`docs/STATUS.md`](docs/STATUS.md) — **START HERE for current state.** Living board: phase progress,
  active work, known bugs, what needs QA, next actions.
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — **the full phase plan (0–7)**: done vs. remaining, and how to
  pick up each remaining phase (spec, branch, validation gate, QA).
- [`ARCHITECTURE.md`](ARCHITECTURE.md) — design & storage contract & paste sequence.
- `docs/PHASE2-LAUNCHER.md` · `PHASE3-PASTE.md` · `PHASE4-EDITOR.md` · `PHASE5-IMPORT-EXPORT.md` ·
  `PHASE6-SETTINGS-WINDOW.md` · `PHASE7-POLISH-PACKAGING.md` — per-phase specs / work orders.
  (Phases 0–1 are described in `ARCHITECTURE.md`.)
- [`docs/BACKLOG.md`](docs/BACKLOG.md) — deferred robustness/UX items.
- `tools/` — screenshot + icon helpers.
