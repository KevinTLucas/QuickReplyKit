# Deferred items / backlog

Small robustness + UX items deliberately deferred to the polish phase (not blocking). Tracked so
they don't get lost.

- **Surface invalid responses everywhere.** A malformed `.json` at the **library root** never shows
  in the UI: "All Responses" lists valid-only and there is no nav node for the root folder, so only
  invalid files inside a subfolder show their ⚠. Consider listing invalid items in "All Responses"
  (muted, ⚠, not paste-able) so users can find and fix broken files anywhere. (QA, Phase 2)
- **Lenient per-field parsing.** One wrong-typed field (e.g. `tags` as a string) currently marks the
  whole response invalid. Never crashes/blocks siblings, but hand-editing support staff could lose a
  response over one typo. Consider per-field leniency / `AllowTrailingCommas` / comment-skip. (QA, Phase 1)
- **Empty-folder-in-empty-library** shows the library-empty message rather than the per-folder empty
  message (whole-library check wins). Deliberate; revisit only if it confuses users. (QA, Phase 2)
- **Watcher post-dispose guard.** Tiny theoretical window where `Changed` can fire just after
  `Dispose`. UI consumer already guards; revisit if it ever bites. (QA, Phase 1)

---

## V2 / Future enhancements (post-MVP — do NOT start until all phases 0–7 are complete)

Requested by the user (2026-09-05). These go beyond the MVP scope (some were explicitly excluded from
the MVP in `PROJECT-BRIEF.md`, e.g. dynamic placeholders) and are intended as a **V2** effort after the
core app ships.

1. **Richer rich-text editor.** Expand the editor toward a lightweight Word-like experience — more
   comprehensive formatting (headings, colors, font size, alignment, etc.) beyond the current
   bold/italic/underline/lists/links/paragraphs subset. Note: this widens the stored HTML subset and
   the CF_HTML/plain converters — keep the storage contract versioned and back-compatible.
2. **Edit the composed message before inserting.** Let the user tweak the final text in-app before it
   goes out — e.g. open the New/Edit editor pre-filled with the selected response/template, ready to
   modify, then insert the edited result (rather than inserting the stored response verbatim).
3. **Dynamic variables that run code.** Template variables whose value is computed at insert time by a
   code block, with the return value inserted — e.g. a `{{greeting}}` that returns "Good morning/
   afternoon/evening" from the current time. **Design/security note:** executing arbitrary code from a
   shareable `.json` library is a real risk (responses are copied between users) — needs a sandbox /
   an allow-listed set of safe built-in functions rather than raw eval. (Conflicts with the MVP's
   "no dynamic values" scope by design; V2 only.)
4. **Response includes / composition.** Let a response reference other saved responses so reusable
   sections chain together, e.g.:
   ```
   {{welcome}}
   Main response content
   {{closing}}
   ```
   Resolve `{{name}}` references (by response name or path) at insert time, with cycle detection and a
   clear error for missing references. Keeps greetings/closings/standard instructions consistent across
   templates.
