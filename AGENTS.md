# Agent handoff

This project is documented so **any** AI agent or developer can pick it up cold, in a fresh session,
with no prior context, and know exactly what's done, what's left, and the open issues.

**Read in this order:**
1. [`CLAUDE.md`](CLAUDE.md) — operating manual: stack, build/test/run/self-test commands, conventions, git
   workflow, how we work (phased delivery + subagent QA + ponytail), and the **Definition of Done** (which
   requires keeping the handoff docs updated on every change).
2. [`docs/STATUS.md`](docs/STATUS.md) — the living state board: `main` tip + test count, everything that's
   **DONE**, the **open work / what's left** (the CI-N continuous-improvement backlog), **known issues**, and
   exactly how to resume.
3. [`docs/ROADMAP.md`](docs/ROADMAP.md) — the full phase plan (0–7 + V2), done vs. remaining, per-item pickup.
4. [`docs/UX-REVIEW.md`](docs/UX-REVIEW.md) — the prioritized continuous-improvement backlog (current "what's left").
5. [`docs/PROJECT-BRIEF.md`](docs/PROJECT-BRIEF.md) — original requirements. [`ARCHITECTURE.md`](ARCHITECTURE.md) — design/contracts.
   [`docs/V2-PLAN.md`](docs/V2-PLAN.md), [`docs/BACKLOG.md`](docs/BACKLOG.md), per-phase specs `docs/PHASE*.md`, `README.md`.

**Current state (2026-09-06):** MVP + all V2 features complete & QA'd; `main` @ `71dc402`, `dotnet test` 384 green.
Open work = the **CI-N** items in STATUS.md / UX-REVIEW.md (CI-1 performance, CI-2 search quality, CI-3 polish).

**Working rules (all detailed in `CLAUDE.md`):** `main` is always green; each change on a branch → implement with the
**ponytail** skill (BCL/native only, no NuGet) → validate → **independent QA by a separate subagent** → merge `--no-ff`.
**On every meaningful change, update the handoff docs** (STATUS.md first) — a change isn't done until code, tests, and
docs are consistent. This discipline is what preserves continuity across sessions and AIs.
