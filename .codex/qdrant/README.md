# Project memory service

This project uses official Qdrant only as compact architectural memory, not as a source-code index.

## Active mode

- MCP: official `mcp-server-qdrant==0.8.1` via offline `uvx` and stdio;
- storage: official local mode at `.codex/data/qdrant` on drive E;
- collection: `gtarp_assistant_memory`;
- embeddings: `sentence-transformers/all-MiniLM-L6-v2`;
- search result limit: 3;
- lifecycle: Codex starts the MCP process on demand; data persists when it stops.

Bootstrap or recheck the memory with:

```powershell
python .\eng\bootstrap-qdrant-memory.py all
```

## Optional Docker mode

Docker Desktop is not required or installed by this setup. If it is installed later, `compose.yaml` provides an isolated service on `127.0.0.1:6333` using `qdrant/qdrant:v1.18.3-unprivileged`, automatic restart and the `gtarp_assistant_qdrant_data` volume. Run `eng/qdrant-memory.ps1 start`, `status`, `restart`, or `stop`. `stop` preserves the volume. Never run `docker compose down --volumes` unless the memory must be intentionally erased.
