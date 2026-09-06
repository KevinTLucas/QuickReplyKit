# QuickReplyKit — V2 Plan (backlog completion + polish)

Completes the [`BACKLOG.md`](BACKLOG.md) items (robustness + the five V2 features), then a continuous
end-user-driven polish pass. Same process as V1: branch per phase (worktree), implement with the
`ponytail` skill, validate + independent QA via a **separate** subagent, merge `--no-ff` when green,
update [`STATUS.md`](STATUS.md). Keep `main` always green. Baseline: `main` MVP-complete, 297 tests.

## Phases

### Phase R — Robustness backlog (Core, small; parallelizable with Phase 8)
- **Lenient per-field parsing**: a single wrong-typed field shouldn't invalidate a whole response;
  tolerate `tags` as string, `favorite`/`created` odd types, trailing commas, comments — recover
  per-field, keep the response valid. Never regress the "invalid file never blocks siblings" contract.
- **Watcher post-dispose guard**: ensure `Changed` can't fire after `Dispose`.
- **Empty-folder-in-empty-library** message: revisit only if it's genuinely confusing (likely leave).

### Phase 8 — Theming & Dark mode (V2-5)
- Extract every inline color into themeable `ResourceDictionary`s (`Themes/Light.xaml`, `Themes/Dark.xaml`):
  window/card backgrounds, borders, accent, primary/muted text, selection, disabled, ⚠/error, hyperlink.
- Convert all views (Launcher, Editor, Settings, Input/Confirm dialogs) to `DynamicResource`. No hardcoded colors left.
- `AppConfig.Theme` = `System | Light | Dark` (default System). Apply at startup + live on change; when
  System, follow Windows (`AppsUseLightTheme`) and react to changes. Settings toggle.
- Theme the rich preview/editor `FlowDocument` (text/background/link) so content is readable in dark.
- QA: screenshots of every window in **both** themes; assert no static color brushes remain; contrast sanity.

### Phase 9 — Template engine: variables + includes (V2-3 safe + V2-4)
- Core `TemplateEngine` resolving `{{ … }}` tokens at **insert time** (rich + plain) and in the preview:
  - **Built-in variables (allow-listed, no code exec):** `{{greeting}}` (Good morning/afternoon/evening),
    `{{date}}`, `{{time}}`, `{{date:<format>}}`, `{{day}}` (extensible registry of pure functions only).
  - **Includes:** a token naming another response inserts that response's (resolved) content — enabling
    `{{welcome}}` / `{{closing}}` chaining. Resolve by response name (then path); **cycle detection** and
    a clear inline error for missing references. Unknown tokens → leave literal or show a clear marker.
  - Escaping for a literal `{{`. Preview shows the resolved result (with a subtle "contains variables" hint).
- **Security:** variables are a fixed built-in set; NO arbitrary/user code is executed (responses are shared).
- QA: each built-in, includes + nesting, cycles, missing refs, literal escape, plain+rich both resolve.

### Phase 10 — Editor upgrades (V2-1 + V2-2)
- **Richer editor (V2-1):** add a sensible "lightweight Word" set — headings/font-size, strikethrough,
  text color + highlight, alignment, maybe indent/hr. Widen the stored HTML subset **additively**
  (old files still load); update `HtmlToFlowDocument` / `FlowDocumentToHtml` / `HtmlText` (plain) / and
  ensure CF_HTML still round-trips. Keep it focused — a support-response editor, not full Word.
- **Edit-before-insert (V2-2):** open the selected response in the editor pre-filled and insert the
  **edited** result without necessarily saving (a "compose & insert" flow / an Insert button in the editor).
- QA: subset round-trip incl. new tags, back-compat load of old files, edit-before-insert reaches the paste path.

### Phase 11+ — Continuous improvement (end-user pass)
After the backlog, a **UX-review subagent** evaluates QuickReplyKit as real end users across realistic
workflows, repeated use, large libraries, keyboard usage, resizing, error handling, import/export,
editing, search, and performance — producing a **prioritized, high-value** improvement list. Implement
the clear wins (impl + QA subagents), reject scope creep, and iterate until no obvious high-value
improvement remains and the app feels complete, polished, and reliable. Candidate areas (validate value
before building): match highlighting in results, "Pasted" feedback toast, most-used sort / usage counts,
richer keyboard affordances, first-run help, preview copy, resilience on network/OneDrive library paths.

## Ordering
Phase R ∥ Phase 8 (disjoint: Core vs views) → Phase 9 → Phase 10 → Phase 11 (continuous). Each merges only when green + QA'd.
