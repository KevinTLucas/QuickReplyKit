# Backlog — future ideas

Everything previously tracked here is **done and shipped** (the MVP robustness items and all V2 features —
richer editor, edit-before-insert, template variables, response includes, dark mode — plus the end-user
continuous-improvement pass). See [`STATUS.md`](STATUS.md) for the current state and known cosmetic caveats.

The list below is **out-of-current-scope future ideas only** — start any of these only if the user asks.

- **Sandboxed user scripting for variables.** Today's template variables are a **fixed, safe allow-list**
  (`{{greeting}}`, `{{date}}`, `{{time}}`, `{{day}}`, `{{date:<fmt>}}`) with **no code execution** — a deliberate
  security boundary, because responses are shareable files. A future "run custom code in a variable" feature would
  need a real sandbox / capability model (never raw eval), and careful UX around importing untrusted templates.
- **Usage analytics / most-used sort.** Optionally track per-response usage counts and offer a "most used" sort or
  view. Low value for a small library; only worthwhile if teams accumulate very large libraries.
- **In-app keyboard cheat-sheet.** A tiny "?" overlay listing the shortcuts (arrows/Enter/Shift+Enter/Ctrl+C, etc.).
  The shortcuts already work and are tooltipped; this is discoverability polish.

## Explicitly considered and declined (kept here so they aren't re-proposed)
- Natural/numeric name sort, number-to-pick (1–9) result selection — unnecessary for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, AI-generated responses — **out of scope
  by design** (see `PROJECT-BRIEF.md`); QuickReplyKit is a local, minimal launcher.
