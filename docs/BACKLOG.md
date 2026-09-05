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
