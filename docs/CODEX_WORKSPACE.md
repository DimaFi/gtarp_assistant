# Codex workspace setup

This repository keeps Codex project policy and project memory local to GTA RP Assistant. Source code remains the authority; Qdrant is only a compact pointer to architecture and non-obvious decisions.

## Files and data

- `.codex/config.toml` — trusted-project defaults and the Qdrant MCP definition.
- `AGENTS.md` — short rules for targeted search, memory, tests and responses.
- `docs/PROJECT_MAP.md` — component-to-file-to-symbol map.
- `.codex/qdrant/bootstrap-memory.json` — reproducible set of 12 initial memories.
- `.codex/data/qdrant` — persistent local collection; ignored by Git.
- `.codex/cache` — `uv` cache/tools, FastEmbed and model caches; ignored by Git.

The collection is `gtarp_assistant_memory`, the embedding model is `sentence-transformers/all-MiniLM-L6-v2`, and one search returns at most three results. Codex may call only `qdrant-find` and `qdrant-store` for this MCP. The pinned MCP starts in `uvx --offline` mode after the initial bootstrap, so normal use does not query PyPI.

## Context policy

Daily work uses medium reasoning, low response verbosity, concise reasoning summaries, a 12 KiB project-document budget and a 6,000-token tool-output limit. Codex starts with an exact `rg` query and reads only the relevant range. It searches Qdrant only when a component location, architectural decision or previous difficult fix is unknown, then verifies the current source file. It stores at most one or two durable memories after a substantial task and never stores code, logs, temporary state or secrets.

No manual compaction threshold or artificial context-window reduction is configured: this Codex version does not document a safe project setting for it, so the model default remains active.

## Profiles

The daily profile is available to the CLI as:

```powershell
codex --profile economy
```

For difficult architecture, concurrency or cross-subsystem debugging, start a temporary high-reasoning session:

```powershell
codex --profile deep
```

Project copies live under `.codex/profiles`; active CLI copies live in `%USERPROFILE%\.codex\economy.config.toml` and `deep.config.toml`. The global base config was not overwritten.

## Verification and recovery

Run a real store/restart/find check:

```powershell
python .\eng\bootstrap-qdrant-memory.py all
```

The command is idempotent, validates that both MCP tools exist, stores the bootstrap only once, starts a second MCP process, checks three semantic queries and confirms that the result count is at most three. A backup of the pre-change user config is retained locally under `.codex/backups` and excluded from Git.

Docker is optional. The active local mode needs no service, port or system installation. See `.codex/qdrant/README.md` for the optional loopback-only Compose alternative.
