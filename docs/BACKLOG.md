# QuickReplyKit - Active Backlog

Only unfinished work belongs here. Completed implementation and QA history is in [`STATUS.md`](STATUS.md) and Git.
Priority is based on crash/core-workflow impact, data-loss or trust-boundary risk, then daily user value.

**Severity:** P0 = crash/data-loss/security; P1 = core workflow or major usability; P2 = valuable enhancement;
P3 = optional polish. **Status:** Confirmed, Investigating, Queued, In progress, QA, or Blocked. A shipped foundation
may still have a Confirmed remaining gap; its merge is noted in the item.

## Active Work

- **B35 · P1 · In progress — Microsoft Word rich-paste fidelity.** Real-Word acceptance rejected the prior build:
  strikethrough, size, color, highlight, alignment, bold, and italic were dropped, and Word-originated formatting could
  appear in the editor but disappear after Save. Root causes are Word's current default **Merge Formatting** paste mode
  and the editor serializer not inheriting formatting stored on RTF paragraphs/list items. Branch
  `fix/word-formatting-compatibility` now requests Word's **Keep Source Formatting** paste operation, preserves block
  formatting during Save, and prefers RTF for New from Clipboard. Automated regression covers every exposed editor
  format and palette value through JSON storage and outgoing RTF; publish and repeat real-Word acceptance before
  merge. Copy followed by a manual Word paste remains controlled by Word's own paste setting and is not the
  QuickReplyKit Paste command.

## Deferred Ideas

- **Usage analytics / most-used sort:** local and opt-in only; only if Recent/Favorites prove insufficient.
- **Keyboard shortcut cheat sheet:** only after onboarding and accessibility validation show a discoverability gap.

## Explicitly Declined

- Natural/numeric name sorting and number-to-pick result selection: insufficient value for named responses.
- CRM integrations, browser extensions, page scraping, ticket automation, and AI-generated responses: outside the
  product brief. QuickReplyKit remains a local response launcher.
