# QuickReplyKit — AI / Developer Operating Manual

> **New session or new AI? Read this file, then [`docs/STATUS.md`](docs/STATUS.md).**
> `CLAUDE.md` tells you **how** to work on this repo. `docs/STATUS.md` tells you **where we are
> right now** (current state, known bugs, what to do next). Between the two you can pick up cold with zero prior context.

## ⚠️ HANDOFF DISCIPLINE — always keep the docs current (non-negotiable)

**Every agent that changes anything in this repo MUST update the handoff docs as part of the same change.**
Continuity across sessions/AIs depends on it — a change is **not done** until code, tests, and docs are consistent.
Do not defer this, do not skip it, even for "small" changes.

**The canonical doc set (this is all of them — keep them accurate, prune anything stale):**

| Doc | What it holds | Update it when… |
|---|---|---|
| [`docs/STATUS.md`](docs/STATUS.md) | **Current state**: `main` tip, test count, what's done, **open work / known bugs**, how to verify | **every meaningful change** (always) |
| [`docs/BACKLOG.md`](docs/BACKLOG.md) | Queued bugs + requested features + future ideas | you add/finish/reprioritize a backlog item |
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | Design, contracts, and the **code map** (where things live) | design/structure/contract changes |
| [`README.md`](README.md) | User-facing guide + how to build/distribute | behaviour, commands, or features change |
| [`docs/PROJECT-BRIEF.md`](docs/PROJECT-BRIEF.md) | Original requirements (source of truth) | (rarely — historical; don't rewrite) |
| [`CLAUDE.md`](CLAUDE.md) / [`AGENTS.md`](AGENTS.md) | How to work + read-order for agents | conventions/workflow/doc-set change |

If you finish a backlog item, move it out of BACKLOG.md and record it in STATUS.md. If you find a new bug, add it to
BACKLOG.md **and** the STATUS.md "known issues" list. The full step-by-step is the **Definition of Done** below.

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
  - `src/QuickReplyKit.Core` — **all headless logic** (model, scanner/watcher/search, HTML/CF_HTML,
    storage, import/export/backups, Trash, diagnostics, response metadata, converters). No UI.
    Fully unit-tested.
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
# Refresh the user-distributable build after every successful main merge:
dotnet publish src/QuickReplyKit -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish
```
**Current QA boundary (mandatory):** do not use Computer Use, UIAutomation, screenshots, screen capture, simulated
mouse/keyboard input, or any other desktop-driving tool. The user keeps the live app on another monitor while work is
underway. Validate with command-line build/tests, focused headless harnesses, self-test output, and static XAML/code
review. Run a Windows paste self-test only when it will not disturb the user's desktop session.
- **Notepad QA hygiene:** if a paste check is explicitly needed, reuse one QA-created Notepad window/tab and close it
  normally in cleanup (`finally` where applicable), choosing **Don't save**. Never launch one Notepad per iteration,
  use force-kill as normal cleanup, move/focus the user's windows, or close a pre-existing user Notepad window.

## Conventions & guardrails (do not violate without a note in STATUS.md)
- **No third-party packages.** BCL / native WPF only — no NuGet for the app or runtime. (Win32 via
  hand-rolled P/Invoke; zips via `System.IO.Compression`; JSON via `System.Text.Json`; tray via
  WinForms `NotifyIcon`; recycle-bin delete via `Microsoft.VisualBasic.FileIO`.) This keeps the
  portable exe lean.
- **Ponytail everything.** Write the shortest correct code; prefer BCL/native over custom; no
  speculative abstractions. Use the `ponytail` skill for writing and `ponytail-review` for review.
- **MVVM-lite**, hand-rolled: `ViewModelBase : INotifyPropertyChanged` + `RelayCommand`. No framework.
- **Storage schema is a contract** (see ARCHITECTURE.md): one `.json` per response, **filename = name**,
  content is a restricted HTML subset — original (bold/italic/underline/ul/ol/li/link/paragraphs) **plus the
  V2 additions** (strikethrough, font-size/heading, text color, highlight, paragraph alignment, all additive
  via inline styles/simple tags). Content may contain **template tokens** `{{…}}` (built-in variables +
  `{{Response Name}}` includes) resolved at insert/preview time by `Core/Templates/TemplateEngine.cs`.
  Keep loading **back-compatible**: the seeded `assets/DefaultLibrary` and any older shared file must still load.
- **DPI:** Per-Monitor-v2 via `app.manifest`. Keep it.

## How we work (process)
- **Phased delivery.** Work proceeds in numbered phases (see STATUS.md). Each phase: implement →
  validate (build/test/focused headless checks/static UI review) → **independent QA** → fix → merge.
- **Subagents.** Implementation is done by a subagent; **QA/validation by a SEPARATE subagent**
  (independence catches the implementer's blind spots). Parallelize independent work with subagents —
  use **git worktree isolation** for agents that would otherwise collide on `bin/obj` or files.
- **Git workflow (strict):**
  - `main` is **always green and launchable.** Never commit half-done work to `main`.
  - Every change is on a concise, human-readable branch that names the work, such as
    `bugfix/backup-restore-integrity` or `feature/folder-trash`. Do not include an AI/vendor name, agent UUID,
    worktree identifier, or other generated session label in a branch name.
  - **One branch/worktree owns one feature or fix.** Do not edit a different feature branch or mix unrelated work
    into the same branch. Shared files may be touched only for the branch's own workflow. Before QA and again before
    merge, audit `git log --oneline main..BRANCH` and `git diff --name-status main...BRANCH`; every branch-only commit
    and file must belong to that feature/fix. Ancestor commits already in `main` are inherited history, not branch work.
  - Merge to `main` with `--no-ff` **only after** build + tests + QA are green. Delete the branch after.
  - After every product/runtime/asset merge to `main`, rerun the integrated build/tests and refresh
    `publish/QuickReplyKit.exe` from that exact tip with the canonical command above. A docs-only merge does not change
    the binary. If the canonical exe is running and locked, do not close it: publish to `publish/latest/QuickReplyKit.exe`
    and promote it on the next unlocked publish. Record that state in `docs/STATUS.md`. Do not pin a bundle hash because
    .NET single-file output is not byte-reproducible between publishes.
  - Commits use only the repository's configured local Git identity. Do not add `Co-Authored-By`, AI/vendor,
    agent, or automation attribution trailers, and do not override `user.name` or `user.email` in commands.
  - `.claude/` (agent worktrees/session data), `bin/`, `obj/`, `*.png`, `publish/` are git-ignored.
- **Definition of Done — the handoff MUST stay current (do this on every meaningful change):**
  1. **Update [`docs/STATUS.md`](docs/STATUS.md)** — the current-state board: `main` tip + test count, what
     changed, what's now DONE, the open work / next actions, and any new known issue. This is what makes
     cold pickup by another AI possible; treat it as part of the change, not an afterthought.
  2. If behaviour, commands, features, or the design changed, update `README.md` (user-facing) and
     [`ARCHITECTURE.md`](ARCHITECTURE.md). If it changes future scope, update [`docs/BACKLOG.md`](docs/BACKLOG.md).
  3. New user-facing capability → add tests; keep `dotnet test` green. Never leave `main` un-green or docs stale.
  4. After a product/runtime/asset merge is green, rebuild the self-contained portable exe from `main`; if the
     canonical file is locked by a running user instance, put the current build in `publish/latest` without touching
     that instance.
  A change isn't "done" until code + tests + these docs are all consistent.

The audited baseline and exact expected test count live only in `docs/STATUS.md`; do not copy a changing test count
elsewhere. A feature commit being merged is not proof that its entire original backlog wording was satisfied: compare
the implementation against the acceptance text and leave any genuine gap in BACKLOG.md.

## Document map (this is the whole set — keep it complete and prune anything that goes stale)
- [`docs/STATUS.md`](docs/STATUS.md) — **START HERE for current state** (what's done, what's open, known caveats, how to verify/extend).
- [`ARCHITECTURE.md`](ARCHITECTURE.md) — design, storage contract, paste sequence, and the **code map** (where things live).
- [`docs/PROJECT-BRIEF.md`](docs/PROJECT-BRIEF.md) — the **original requirements** (source of truth for what the app should do).
- [`README.md`](README.md) — user-facing guide.
- [`docs/BACKLOG.md`](docs/BACKLOG.md) — unfinished requested work and explicitly deferred future ideas
  (completed implementation and QA history is in STATUS and Git).
- `tools/` — optional legacy screenshot helpers (do not use while the current no-desktop-automation directive is in
  force) and `make-icon.ps1`.

> Historical per-phase work orders (PHASE*, ROADMAP, V2-PLAN, UX-REVIEW) were removed once the work shipped — they'd
> only bloat a fresh pickup. The build history lives in git; the docs above are the living, authoritative set.
