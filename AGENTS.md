# Agent handoff

This project is documented so **any** AI agent or developer can pick it up cold, in a fresh session,
with no prior context.

**Read in this order:**
1. [`CLAUDE.md`](CLAUDE.md) — operating manual: stack, build/test/run commands, conventions, git
   workflow, and how we work (phased delivery + subagent QA + ponytail).
2. [`docs/STATUS.md`](docs/STATUS.md) — the living state board: what's done, what's in progress,
   **known bugs / top priority**, what needs QA, and the ordered next actions.
3. [`ARCHITECTURE.md`](ARCHITECTURE.md) — design and the storage/rich-text/paste contracts.

Everything in `CLAUDE.md` applies regardless of which model/tool you are. Keep `docs/STATUS.md`
updated at the end of each work session — that is what preserves continuity across sessions.
