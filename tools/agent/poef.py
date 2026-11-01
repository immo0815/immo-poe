#!/usr/bin/env python3
"""The cloud side of PoEformance's agent link: a Claude session using the tool on the player's own Windows PC.

The tool serves the Model Context Protocol on a Cloudflare quick tunnel (src/PoEformance.Features/AgentLink.cs) and
publishes the tunnel's address on this repository's ``agent-link`` branch. This script finds that address with git,
adds the key every request must carry, and forwards - as an MCP server over stdio for Claude Code (``.mcp.json``
starts it as ``poef.py mcp``), or from a shell::

    python3 tools/agent/poef.py link                         # where the tool is, and whether it answers
    python3 tools/agent/poef.py call status
    python3 tools/agent/poef.py call monster_book '{"monster": "Malgor", "stage": "built"}'
    python3 tools/agent/poef.py download <id> [<id> ...]     # files a call offered (a capture's zip pieces)
    python3 tools/agent/poef.py login                        # keeps $POEF_AGENT_KEY in a file for the MCP server

The key is $POEF_AGENT_KEY (the cloud environment's settings; the tool's About page copies it), or the file
~/.config/poeformance/agent-key. $POEF_AGENT_URL names the tool's address outright instead of the branch (a local
test). Standard library only, so it runs in any container without installing anything.
"""

from __future__ import annotations

import base64
import json
import os
import pathlib
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import zipfile

REPO = pathlib.Path(__file__).resolve().parents[2]
BRANCH = "agent-link"
BEACON = "agent-link.json"
KEY_VARIABLE = "POEF_AGENT_KEY"
KEY_FILE = pathlib.Path.home() / ".config" / "poeformance" / "agent-key"
WORK = pathlib.Path(os.environ.get("POEF_AGENT_DIR", "/tmp/poeformance"))
TOOLS_SNAPSHOT = pathlib.Path(__file__).with_name("tools.json")
PROTOCOL = "2025-06-18"

# One request waits this long for an answer; the tool answers within 40 s (a long call as pending), and Cloudflare
# closes a request whose answer has not begun after 100.
REQUEST_SECONDS = 90


class Unreachable(Exception):
    """The tool did not answer: the PC is off, the tool or its link is off, or the tunnel is being made anew."""


# ── Where the tool is ───────────────────────────────────────────────────────────────────────────


class Link:
    """The tool's address and key, found again whenever the tool stops answering at the old address."""

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._beacon: dict | None = None
        self._fetched = 0.0
        self.offered: list[dict] = []

    def key(self) -> str:
        key = os.environ.get(KEY_VARIABLE, "").strip()
        if not key and KEY_FILE.exists():
            key = KEY_FILE.read_text(encoding="utf-8").strip()
        return key

    def beacon(self, again: bool = False) -> dict:
        """The published address, fetched from the agent-link branch (again only where asked and not in the last 5 s)."""
        # A tool reached some other way - a local test, or a tunnel of one's own - named outright.
        if os.environ.get("POEF_AGENT_URL"):
            return {"url": os.environ["POEF_AGENT_URL"], "since": "", "version": "", "commit": "", "key": ""}
        with self._lock:
            if self._beacon is not None and not again:
                return self._beacon
            if again and time.monotonic() - self._fetched < 5 and self._beacon is not None:
                return self._beacon
            self._fetched = time.monotonic()
            fetched = subprocess.run(
                ["git", "-C", str(REPO), "fetch", "--quiet", "--depth", "1", "origin",
                 f"+refs/heads/{BRANCH}:refs/remotes/origin/{BRANCH}"],
                capture_output=True, text=True, timeout=60)
            if fetched.returncode != 0:
                raise Unreachable(
                    f"the {BRANCH} branch could not be fetched - the tool has not published an address yet "
                    f"(its agent link off, or its GitHub token may not write): {fetched.stderr.strip()}")
            shown = subprocess.run(
                ["git", "-C", str(REPO), "show", f"refs/remotes/origin/{BRANCH}:{BEACON}"],
                capture_output=True, text=True, timeout=30)
            if shown.returncode != 0:
                raise Unreachable(f"the {BRANCH} branch holds no {BEACON}: {shown.stderr.strip()}")
            self._beacon = json.loads(shown.stdout)
            return self._beacon

    def post(self, message: dict) -> dict | None:
        """One JSON-RPC message to the tool; its answer, or None for a notification. Finds the tool again once."""
        body = json.dumps(message).encode("utf-8")
        for attempt in (0, 1):
            beacon = self.beacon(again=attempt > 0)
            try:
                answer = self._send(beacon["url"].rstrip("/") + "/mcp", body)
                return json.loads(answer) if answer else None
            except Unreachable:
                if attempt == 1:
                    raise
        return None

    def _send(self, url: str, body: bytes | None) -> bytes:
        return self._answer(url, body)[0]

    def _answer(self, url: str, body: bytes | None) -> tuple[bytes, dict]:
        key = self.key()
        if not key:
            raise Unreachable(
                f"no key: put the tool's key (its About page, agent link, copy key) in the cloud environment as {KEY_VARIABLE},"
                f" or run 'python3 tools/agent/poef.py login' where the variable is set")
        request = urllib.request.Request(
            url, data=body, method="POST" if body is not None else "GET",
            headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json",
                     "Accept": "application/json, text/event-stream", "User-Agent": "poeformance-agent-bridge/1"})
        try:
            with urllib.request.urlopen(request, timeout=REQUEST_SECONDS) as response:
                return response.read(), dict(response.headers.items())
        except urllib.error.HTTPError as error:
            said = error.read().decode("utf-8", "replace")[:300]
            if error.code == 401:
                beacon = self._beacon or {}
                raise Unreachable(
                    f"the tool refused the key (401). The tool's key is {beacon.get('key', '?')}..., this key's starts "
                    f"{fingerprint(key)} - copy the key again from the About page into {KEY_VARIABLE}") from error
            if error.code in (502, 503, 504, 530) or "1033" in said:
                raise Unreachable(f"the tunnel answered {error.code}: the tool or its tunnel is down ({said.strip()})") from error
            raise Unreachable(f"the tool answered {error.code}: {said}") from error
        except (urllib.error.URLError, TimeoutError, ConnectionError, OSError) as error:
            raise Unreachable(f"the tool could not be reached at {url}: {error}") from error

    def fetch(self, file_id: str, folder: pathlib.Path, name: str) -> pathlib.Path:
        """One offered file into the folder, named as the tool names it; its path."""
        for attempt in (0, 1):
            beacon = self.beacon(again=attempt > 0)
            try:
                data, headers = self._answer(beacon["url"].rstrip("/") + "/files/" + file_id, None)
                given = headers.get("Content-Disposition", "")
                if not name and 'filename="' in given:
                    name = given.split('filename="', 1)[1].split('"', 1)[0]
                target = folder / pathlib.Path(name or file_id).name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
                return target
            except Unreachable:
                if attempt == 1:
                    raise
        raise Unreachable("the file could not be fetched")


def fingerprint(key: str) -> str:
    import hashlib
    return hashlib.sha256(key.encode("utf-8")).hexdigest()[:8] if key else ""


LINK = Link()


# ── Calls ───────────────────────────────────────────────────────────────────────────────────────


def call(name: str, arguments: dict, progress=None) -> dict:
    """A tool call, collected whole: a pending answer is asked after until the job ends."""
    answer = LINK.post({"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": {"name": name, "arguments": arguments}})
    started = time.monotonic()
    while True:
        if answer is None:
            return error_result("the tool sent no answer")
        if "error" in answer:
            return error_result(f"{answer['error'].get('message', 'error')} ({answer['error'].get('code')})")
        result = answer.get("result", {})
        pending = (result.get("structuredContent") or {}).get("pending")
        if not pending:
            remember(result)
            return result
        if progress is not None:
            progress(time.monotonic() - started, text_of(result))
        try:
            answer = LINK.post({"jsonrpc": "2.0", "id": 1, "method": "poeformance/wait", "params": {"job": pending, "seconds": 40}})
        except Unreachable as unreachable:
            # The tool restarted under the job (an update), or the tunnel was made anew: the job is gone with it.
            return error_result(f"the tool stopped answering while '{name}' ran (job {pending}): {unreachable}")


OFFERED = WORK / "offered.json"


def remember(result: dict) -> None:
    """Keeps the files a call offered, in memory and on disk, so a download - this process's or a later one's - names them."""
    files = (result.get("structuredContent") or {}).get("files")
    if files:
        LINK.offered = files
        WORK.mkdir(parents=True, exist_ok=True)
        OFFERED.write_text(json.dumps(files), encoding="utf-8")


def offered() -> list[dict]:
    if LINK.offered:
        return LINK.offered
    try:
        return json.loads(OFFERED.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return []


def error_result(text: str) -> dict:
    return {"content": [{"type": "text", "text": text}], "isError": True}


def text_of(result: dict) -> str:
    return "\n".join(part.get("text", "") for part in result.get("content", []) if part.get("type") == "text")


def download(ids: list[str]) -> str:
    """Fetches offered files into WORK; zip pieces are joined and every zip unpacked beside itself."""
    listed = offered()
    known = {file["id"]: file for file in listed}
    wanted = ids or [file["id"] for file in listed]
    if not wanted:
        return "nothing to download - no call has offered files yet; name the ids a call listed"
    folder = WORK / "files"
    fetched: list[pathlib.Path] = []
    for file_id in wanted:
        fetched.append(LINK.fetch(file_id, folder, known.get(file_id, {}).get("name", "")))
    lines = [f"{path} ({path.stat().st_size:,} bytes)" for path in fetched]

    # Pieces of one zip (name.zip.001, .002, ...) are its bytes in order: joined by concatenation.
    pieces: dict[str, list[pathlib.Path]] = {}
    for path in fetched:
        stem, _, number = path.name.rpartition(".")
        if stem.endswith(".zip") and number.isdigit() and len(number) == 3:
            pieces.setdefault(stem, []).append(path)
    zips = [path for path in fetched if path.suffix == ".zip"]
    for stem, parts in pieces.items():
        joined = folder / stem
        with joined.open("wb") as out:
            for part in sorted(parts, key=lambda one: one.name):
                out.write(part.read_bytes())
        zips.append(joined)
        lines.append(f"joined {len(parts)} piece(s) into {joined}")
    for archive in zips:
        unpacked = archive.with_suffix("")
        try:
            with zipfile.ZipFile(archive) as opened:
                opened.extractall(unpacked)
            lines.append(f"unpacked {archive.name} into {unpacked}")
        except zipfile.BadZipFile as bad:
            lines.append(f"{archive.name} did not unpack ({bad}) - a piece may be missing")
    return "\n".join(lines)


def link_said() -> str:
    lines = []
    key = LINK.key()
    lines.append(f"key: {'set, fingerprint ' + fingerprint(key) if key else 'NOT SET - ' + KEY_VARIABLE + ' or ' + str(KEY_FILE)}")
    try:
        beacon = LINK.beacon(again=True)
    except Unreachable as unreachable:
        return "\n".join(lines + [str(unreachable)])
    if os.environ.get("POEF_AGENT_URL"):
        lines.append(f"named by POEF_AGENT_URL: {beacon['url']}")
    else:
        lines.append(f"published: {beacon.get('url')} since {beacon.get('since')} by {beacon.get('version')} ({beacon.get('commit', '')[:12]})")
    if key and beacon.get("key") and beacon["key"] != fingerprint(key):
        lines.append(f"the published key fingerprint {beacon['key']} is not this key's - copy the key again")
    try:
        started = time.monotonic()
        LINK.post({"jsonrpc": "2.0", "id": 1, "method": "ping"})
        lines.append(f"the tool answers ({(time.monotonic() - started) * 1000:.0f} ms)")
    except Unreachable as unreachable:
        lines.append(f"the tool does not answer: {unreachable}")
    return "\n".join(lines)


# ── The MCP server over stdio ───────────────────────────────────────────────────────────────────

LOCAL_TOOLS = [
    {"name": "download",
     "description": "Fetches files a call offered (a capture's zip pieces, a screenshot at full size) into the container; joins a zip's"
                    " pieces and unpacks it. Answers with the local paths - read the unpacked capture with the file tools.",
     "inputSchema": {"type": "object", "properties": {
         "ids": {"type": "array", "items": {"type": "string"}, "description": "The file ids a call listed; default every file the last call offered."}}}},
    {"name": "link",
     "description": "Where the tool's link is: the key set here, the address the tool published and whether the tool answers there."
                    " Call it when a tool says the tool cannot be reached.",
     "inputSchema": {"type": "object", "properties": {}}},
]


class Server:
    def __init__(self) -> None:
        self._out = threading.Lock()
        self._workers: list[threading.Thread] = []

    def send(self, message: dict) -> None:
        line = json.dumps(message, separators=(",", ":"))
        with self._out:
            sys.stdout.write(line + "\n")
            sys.stdout.flush()

    def tools(self) -> list[dict]:
        listed = json.loads(TOOLS_SNAPSHOT.read_text(encoding="utf-8")) if TOOLS_SNAPSHOT.exists() else []
        return listed + LOCAL_TOOLS

    def handle(self, message: dict) -> None:
        method = message.get("method")
        ident = message.get("id")
        if ident is None:
            return  # a notification, or a response to nothing this server asks
        try:
            if method == "initialize":
                asked = (message.get("params") or {}).get("protocolVersion") or PROTOCOL
                self.reply(ident, {
                    "protocolVersion": asked,
                    "capabilities": {"tools": {"listChanged": False}},
                    "serverInfo": {"name": "poeformance", "version": "bridge-1"},
                    "instructions": ("PoEformance v2 on the player's own Windows PC, beside Path of Exile 2, reached through a tunnel."
                                     " Call status first. The tools drive the tool, never the game. A capture hides the overlay a"
                                     " moment and may use every core; update install restarts the tool (back within a minute or two)."
                                     " Files a call offers are fetched with download.")})
            elif method == "ping":
                self.reply(ident, {})
            elif method == "tools/list":
                self.reply(ident, {"tools": self.tools()})
            elif method == "tools/call":
                params = message.get("params") or {}
                name = params.get("name", "")
                arguments = params.get("arguments") or {}
                token = (params.get("_meta") or {}).get("progressToken")
                self.reply(ident, self.call(name, arguments, token))
            else:
                self.error(ident, -32601, f"'{method}' is not a method this server knows")
        except Exception as failure:  # noqa: BLE001 - every failure becomes an answer, never a dead server
            self.error(ident, -32603, f"{type(failure).__name__}: {failure}")

    def call(self, name: str, arguments: dict, token) -> dict:
        try:
            if name == "download":
                return {"content": [{"type": "text", "text": download(list(arguments.get("ids") or []))}], "isError": False}
            if name == "link":
                return {"content": [{"type": "text", "text": link_said()}], "isError": False}

            def progress(seconds: float, said: str) -> None:
                if token is not None:
                    self.send({"jsonrpc": "2.0", "method": "notifications/progress",
                               "params": {"progressToken": token, "progress": seconds, "message": said[:200]}})

            return call(name, arguments, progress)
        except Unreachable as unreachable:
            return error_result(f"the tool cannot be reached: {unreachable}")

    def reply(self, ident, result: dict) -> None:
        self.send({"jsonrpc": "2.0", "id": ident, "result": result})

    def error(self, ident, code: int, text: str) -> None:
        self.send({"jsonrpc": "2.0", "id": ident, "error": {"code": code, "message": text}})

    def run(self) -> None:
        for line in sys.stdin:
            line = line.strip()
            if not line:
                continue
            try:
                message = json.loads(line)
            except json.JSONDecodeError:
                self.error(None, -32700, "not JSON")
                continue
            # Each call on a thread of its own: a capture runs for minutes, and a status asked meanwhile must not wait.
            worker = threading.Thread(target=self.handle, args=(message,), daemon=True)
            worker.start()
            self._workers = [one for one in self._workers if one.is_alive()] + [worker]

        # Input closed: what was asked last is answered before going, a short call at least - a capture is not waited for.
        for worker in self._workers:
            worker.join(timeout=30)


# ── From a shell ────────────────────────────────────────────────────────────────────────────────


def shell(arguments: list[str]) -> int:
    if not arguments or arguments[0] in ("-h", "--help", "help"):
        print(__doc__)
        return 0
    verb = arguments[0]
    if verb == "mcp":
        Server().run()
        return 0
    if verb == "link":
        print(link_said())
        return 0
    if verb == "login":
        key = os.environ.get(KEY_VARIABLE, "").strip()
        if not key:
            print(f"{KEY_VARIABLE} is not set here", file=sys.stderr)
            return 1
        KEY_FILE.parent.mkdir(parents=True, exist_ok=True)
        KEY_FILE.write_text(key, encoding="utf-8")
        KEY_FILE.chmod(0o600)
        print(f"kept in {KEY_FILE} (fingerprint {fingerprint(key)})")
        return 0
    if verb == "download":
        try:
            print(download(arguments[1:]))
            return 0
        except Unreachable as unreachable:
            print(f"the tool cannot be reached: {unreachable}", file=sys.stderr)
            return 1
    if verb == "call" and len(arguments) >= 2:
        given = json.loads(arguments[2]) if len(arguments) > 2 else {}
        try:
            result = call(arguments[1], given, lambda seconds, said: print(f"... {seconds:.0f} s: {said}", file=sys.stderr))
        except Unreachable as unreachable:
            print(f"the tool cannot be reached: {unreachable}", file=sys.stderr)
            return 1
        pictures = WORK / "pictures"
        for part in result.get("content", []):
            if part.get("type") == "image":
                pictures.mkdir(parents=True, exist_ok=True)
                path = pictures / f"{arguments[1]}-{time.strftime('%Y%m%d-%H%M%S')}-{int(time.time() * 1000) % 1000:03d}.png"
                path.write_bytes(base64.b64decode(part["data"]))
                print(f"[picture: {path}]")
            elif part.get("type") == "text":
                print(part.get("text", ""))
        files = (result.get("structuredContent") or {}).get("files")
        if files:
            print("download them with: python3 tools/agent/poef.py download " + " ".join(file["id"] for file in files))
        return 1 if result.get("isError") else 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(shell(sys.argv[1:]))
