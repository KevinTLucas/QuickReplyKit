# Agent handoff

This project is documented so **any** AI agent or developer can pick it up cold, with no prior context, and
know exactly what it is, how it's built, and its current state. The doc set is intentionally small.

**Read in this order:**
1. [`CLAUDE.md`](CLAUDE.md) — operating manual: stack, build/test/run/self-test commands, conventions, git
   workflow, how we work (branch per change + **ponytail** + **independent QA by a separate subagent**), and the
   **Definition of Done** (which requires keeping the handoff docs updated on every change).
2. [`docs/STATUS.md`](docs/STATUS.md) — current state: what's done, what's open, known caveats, how to verify/extend.
3. [`ARCHITECTURE.md`](ARCHITECTURE.md) — design, contracts, and the code map (where things live).
4. [`docs/PROJECT-BRIEF.md`](docs/PROJECT-BRIEF.md) — original requirements. [`README.md`](README.md) — user guide.
   [`docs/BACKLOG.md`](docs/BACKLOG.md) — out-of-scope future ideas only.

**Current state (2026-09-06):** ✅ COMPLETE — MVP + all V2 features + the end-user polish pass are done and merged;
`dotnet test` = 403 green; no open work. New work = new feature requests (BACKLOG), started only when the user asks.

**Golden rules (detailed in `CLAUDE.md`):** `main` is always green; each change on a branch → ponytail (BCL/native
only, no NuGet) → separate-subagent QA → merge `--no-ff`; **update the handoff docs (STATUS.md first) on every change** —
a change isn't done until code, tests, and docs are consistent. That discipline is what preserves continuity.
