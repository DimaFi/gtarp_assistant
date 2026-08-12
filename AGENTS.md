# Codex rules for GTA RP Assistant

## Context search

- Work only within the current task. Do not scan the whole repository without a concrete reason.
- Start with exact `rg` searches for a file, symbol, import, test, or error text. Read only the relevant range of large files.
- Use `qdrant-find` once when a component location is unknown, an architectural decision must be recalled, or a previous non-obvious bug may apply. Treat results as pointers and verify the current source file before editing.
- Skip Qdrant when the file/symbol is already known or present in the active context.
- Never read `artifacts`, `bin`, `obj`, caches, generated output, logs, binaries, `.venv`, or `node_modules` wholesale. Respect `.gitignore`.

## Memory

- Store only durable architecture, contracts, component paths, constraints, or non-obvious fixes. Keep each entry under roughly 700 characters and include component, file, symbol, and decision.
- Do not store source files, diffs, logs, test output, temporary task state, obvious facts, personal data, credentials, tokens, or keys.
- Usually store at most 1–2 memories after a substantial completed task; never after every edit.

## Changes and verification

- Preserve unrelated user changes in a dirty worktree. Use `apply_patch` for manual edits.
- Run the narrowest relevant test first. Run `eng/build.ps1 -Configuration Release -Runtime win-x64` only for cross-cutting or release-ready changes.
- Summarize successful tests; on failure capture only the relevant error and stack frame.
- Keep product documentation synchronized according to `docs/DOCUMENTATION_INDEX.md`.
- Do not spawn subagents unless the task contains independent complex work where delegation provides clear value, or the user explicitly asks for them.

## Response

- Keep the final answer compact: what changed, important files, verification, and remaining issues.
