# Agent handoff

This project is documented so **any** AI agent or developer can pick it up cold, with no prior context, and
know exactly what it is, how it's built, and its current state. The doc set is intentionally small.

> ## ⚠️ NON-NEGOTIABLE: keep the handoff docs current on EVERY change
> Update the docs as part of the same change that touches code — **a change is not done until code, tests, and docs are
> consistent.** At minimum update [`docs/STATUS.md`](docs/STATUS.md) (state, `main` tip, test count, open work / known
> bugs); move finished items out of [`docs/BACKLOG.md`](docs/BACKLOG.md) and log new bugs into both. See `CLAUDE.md` →
> "HANDOFF DISCIPLINE" for the doc-set table and the Definition of Done. This discipline is what preserves continuity
> across sessions and AIs — do not skip it, even for small changes.

**Read in this order:**
1. [`CLAUDE.md`](CLAUDE.md) — operating manual: stack, build/test/run/self-test commands, conventions, git
   workflow, how we work (branch per change + **ponytail** + **independent QA by a separate subagent**), and the
   **Definition of Done** (which requires keeping the handoff docs updated on every change).
2. [`docs/STATUS.md`](docs/STATUS.md) — current state: what's done, what's open, known caveats, how to verify/extend.
3. [`ARCHITECTURE.md`](ARCHITECTURE.md) — design, contracts, and the code map (where things live).
4. [`docs/PROJECT-BRIEF.md`](docs/PROJECT-BRIEF.md) — original requirements. [`README.md`](README.md) — user guide.
   [`docs/BACKLOG.md`](docs/BACKLOG.md) — unfinished requested work plus explicitly deferred ideas.

**Current state (audited 2026-09-07):** `main` includes completed B01-B08 and is green at 519 tests. The app is launchable, but
the backlog is **not empty**: removal of folder tags leads the remaining work, followed
by the requested UI/workflow improvements and sandboxed scripts. Read `docs/STATUS.md` for the current evidence and
`docs/BACKLOG.md` for the exact next item.

**Golden rules (detailed in `CLAUDE.md`):** `main` is always green; each change on a branch → ponytail (BCL/native
only, no NuGet) → separate-subagent QA → merge `--no-ff`; **update the handoff docs (STATUS.md first) on every change** —
a change isn't done until code, tests, and docs are consistent. That discipline is what preserves continuity.
Use concise, human-readable branch names that describe the feature or bug; never include an AI/vendor name, agent UUID,
or generated worktree/session identifier. One branch/worktree owns exactly one feature or fix: never work in another
feature's branch or mix unrelated changes. Before QA and merge, verify the branch-owned commits and files with
`git log --oneline main..BRANCH` and `git diff --name-status main...BRANCH`; inherited `main` history is not branch work.
Commits use only the configured local Git identity: never add `Co-Authored-By` or other AI/agent attribution trailers,
and never override the repository's Git author configuration.
After every merge to `main`, rerun the integrated build/tests and rebuild the self-contained
`publish/QuickReplyKit.exe` from that exact tip; record successful verification in `docs/STATUS.md` rather than a
bundle hash, which is not byte-reproducible.

**QA cleanup:** reuse a single Notepad tab for paste verification and close every test-created tab normally with
**Don't save** before finishing. Do not spawn a new Notepad per loop or use force-kill as normal cleanup, because
Windows 11 session restore preserves those tabs. Verify no QA-created Notepad/QuickReplyKit windows remain.

**Current QA boundary:** do not use Computer Use, UIAutomation, screenshots, screen capture, simulated input, or
other desktop-driving tools. Do not move, focus, or close the user's windows. Use command-line build/tests, focused
headless harnesses, self-test output when it will not disturb the desktop, and static XAML/code review.
