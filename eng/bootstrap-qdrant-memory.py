"""Bootstrap and verify the project-scoped Qdrant MCP memory.

Uses the MCP stdio JSON-RPC protocol directly so the validation does not depend
on a running Codex session. Runtime data and downloaded models stay under
<project>/.codex, which is ignored by Git.
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import threading
import time
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
MEMORY_FILE = ROOT / ".codex" / "qdrant" / "bootstrap-memory.json"
LOCAL_PATH = ROOT / ".codex" / "data" / "qdrant"
CACHE_ROOT = ROOT / ".codex" / "cache"
COLLECTION = "gtarp_assistant_memory"
SENTINEL = "memory_set=bootstrap-v1"


class McpClient:
    def __init__(self, timeout_seconds: int = 180) -> None:
        env = os.environ.copy()
        env.update(
            {
                "QDRANT_LOCAL_PATH": str(LOCAL_PATH),
                "COLLECTION_NAME": COLLECTION,
                "QDRANT_SEARCH_LIMIT": "3",
                "EMBEDDING_PROVIDER": "fastembed",
                "EMBEDDING_MODEL": "sentence-transformers/all-MiniLM-L6-v2",
                "FASTEMBED_CACHE_PATH": str(CACHE_ROOT / "fastembed"),
                "HF_HOME": str(CACHE_ROOT / "huggingface"),
                "UV_CACHE_DIR": str(CACHE_ROOT / "uv"),
                "UV_TOOL_DIR": str(CACHE_ROOT / "uv-tools"),
                "FASTMCP_LOG_LEVEL": "WARNING",
            }
        )
        self.timeout_seconds = timeout_seconds
        self._next_id = 1
        self._stderr: list[str] = []
        self.process = subprocess.Popen(
            ["uvx", "--offline", "--from", "mcp-server-qdrant==0.8.1", "mcp-server-qdrant"],
            cwd=ROOT,
            env=env,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1,
        )
        threading.Thread(target=self._drain_stderr, daemon=True).start()

    def _drain_stderr(self) -> None:
        assert self.process.stderr is not None
        for line in self.process.stderr:
            self._stderr.append(line.rstrip())

    def _send(self, payload: dict[str, Any]) -> None:
        assert self.process.stdin is not None
        self.process.stdin.write(json.dumps(payload, ensure_ascii=False) + "\n")
        self.process.stdin.flush()

    def request(self, method: str, params: dict[str, Any] | None = None) -> Any:
        request_id = self._next_id
        self._next_id += 1
        self._send({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params or {}})
        deadline = time.monotonic() + self.timeout_seconds
        assert self.process.stdout is not None
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                diagnostics = "\n".join(self._stderr[-20:])
                raise RuntimeError(f"MCP server exited with {self.process.returncode}.\n{diagnostics}")
            line = self.process.stdout.readline()
            if not line:
                continue
            message = json.loads(line)
            if message.get("id") != request_id:
                continue
            if "error" in message:
                raise RuntimeError(json.dumps(message["error"], ensure_ascii=False))
            return message.get("result")
        raise TimeoutError(f"Timed out waiting for MCP method {method}")

    def notify(self, method: str, params: dict[str, Any] | None = None) -> None:
        self._send({"jsonrpc": "2.0", "method": method, "params": params or {}})

    def initialize(self) -> list[str]:
        self.request(
            "initialize",
            {
                "protocolVersion": "2025-03-26",
                "capabilities": {},
                "clientInfo": {"name": "gtarp-memory-bootstrap", "version": "1.0"},
            },
        )
        self.notify("notifications/initialized")
        result = self.request("tools/list")
        return [tool["name"] for tool in result.get("tools", [])]

    def call_tool(self, name: str, arguments: dict[str, Any]) -> Any:
        return self.request("tools/call", {"name": name, "arguments": arguments})

    def close(self) -> None:
        if self.process.poll() is None:
            if self.process.stdin:
                self.process.stdin.close()
            try:
                self.process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=10)

    def __enter__(self) -> "McpClient":
        return self

    def __exit__(self, *_: object) -> None:
        self.close()


def flatten_text(result: Any) -> str:
    return "\n".join(
        item.get("text", "")
        for item in (result or {}).get("content", [])
        if item.get("type") == "text"
    )


def result_count(result: Any) -> int:
    return sum(1 for item in (result or {}).get("content", []) if item.get("type") == "text")


def open_client() -> McpClient:
    client = McpClient()
    tools = client.initialize()
    required = {"qdrant-find", "qdrant-store"}
    if not required.issubset(tools):
        client.close()
        raise RuntimeError(f"Unexpected MCP tools: {tools}")
    return client


def bootstrap() -> None:
    records = json.loads(MEMORY_FILE.read_text(encoding="utf-8"))
    if not 8 <= len(records) <= 15:
        raise ValueError("Bootstrap must contain between 8 and 15 records")
    if any(len(record["information"]) > 700 for record in records):
        raise ValueError("Every memory must be at most 700 characters")

    with open_client() as client:
        existing = flatten_text(client.call_tool("qdrant-find", {"query": SENTINEL}))
        if SENTINEL in existing:
            print(f"Bootstrap already present in {COLLECTION}; no records added.")
            return
        for record in records:
            client.call_tool("qdrant-store", record)
        print(f"Stored {len(records)} compact memories in {COLLECTION}.")


def verify() -> None:
    queries = {
        "composition root dependency wiring": "App.xaml.cs",
        "micro model benchmark decision": "ADR-0001",
        "microphone Qwen whisper migration bug": "ProviderSettingsMigration",
    }
    with open_client() as client:
        for query, expected in queries.items():
            result = client.call_tool("qdrant-find", {"query": query})
            count = result_count(result)
            if count > 3:
                raise RuntimeError(f"Query '{query}' returned {count} results; expected at most 3.")
            text = flatten_text(result)
            if expected not in text:
                raise RuntimeError(f"Query '{query}' did not return expected marker '{expected}'.")
            print(f"PASS: {query} -> {expected} ({count} result messages)")
    if not LOCAL_PATH.exists():
        raise RuntimeError(f"Persistent local path was not created: {LOCAL_PATH}")
    print(f"Persistence path: {LOCAL_PATH}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("bootstrap", "verify", "all"), default="all", nargs="?")
    args = parser.parse_args()
    if args.action in {"bootstrap", "all"}:
        bootstrap()
    if args.action in {"verify", "all"}:
        verify()
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"ERROR: {error}", file=sys.stderr)
        raise SystemExit(1)
