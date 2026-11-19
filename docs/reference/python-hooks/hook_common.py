import json
import os
import subprocess
import sys
from datetime import datetime
from pathlib import Path

STATE_DIR = Path(__file__).parent / "state"
STATE_DIR.mkdir(exist_ok=True)
TRACE_FILE = STATE_DIR / "hook_trace.log"
BLOCK_COUNTS_FILE = STATE_DIR / "hook_block_counts.json"
MAX_BLOCK_COUNT = 3
COMMAND_TIMEOUT_SEC = 120
MAX_OUTPUT_CHARS = 8000


def trace(message: str) -> None:
    timestamp = datetime.now().strftime("%H:%M:%S")
    with TRACE_FILE.open("a", encoding="utf-8") as trace_file:
        trace_file.write(f"{timestamp} {message}\n")


def read_hook_input() -> dict:
    if sys.stdin.isatty():
        return {}
    raw_input = sys.stdin.read()
    if not raw_input.strip():
        return {}
    try:
        return json.loads(raw_input)
    except json.JSONDecodeError:
        return {"raw_input": raw_input}


def read_block_counts() -> dict:
    if not BLOCK_COUNTS_FILE.exists():
        return {}
    try:
        return json.loads(BLOCK_COUNTS_FILE.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return {}


def build_block_count_key(session_id: str, hook_name: str) -> str:
    return f"{session_id}:{hook_name}"


def write_block_counts(block_counts: dict) -> None:
    BLOCK_COUNTS_FILE.write_text(json.dumps(block_counts, indent=2), encoding="utf-8")


def increment_block_count(block_count_key: str) -> int:
    block_counts = read_block_counts()
    block_counts[block_count_key] = block_counts.get(block_count_key, 0) + 1
    write_block_counts(block_counts)
    return block_counts[block_count_key]


def reset_block_count(block_count_key: str) -> None:
    block_counts = read_block_counts()
    if block_count_key not in block_counts:
        return
    del block_counts[block_count_key]
    write_block_counts(block_counts)


def can_block(block_count_key: str) -> bool:
    return read_block_counts().get(block_count_key, 0) < MAX_BLOCK_COUNT


def block(block_count_key: str, message: str) -> int:
    block_count = increment_block_count(block_count_key)
    trace(f"[{block_count_key}] blocage {block_count}/{MAX_BLOCK_COUNT}")
    print(message, file=sys.stderr)
    return 2


def run_command(command: str) -> tuple[int, str]:
    project_dir = os.environ.get("CLAUDE_PROJECT_DIR", str(Path(__file__).parent.parent))
    completed_process = subprocess.run(
        command,
        shell=True,
        cwd=project_dir,
        capture_output=True,
        text=True,
        timeout=COMMAND_TIMEOUT_SEC,
    )
    return completed_process.returncode, completed_process.stdout + completed_process.stderr


def build_block_message(command: str, command_output: str, rules_prompt: str) -> str:
    truncated_output = command_output.strip()[:MAX_OUTPUT_CHARS]
    return (
        f"La commande `{command}` a signalé des erreurs :\n\n"
        f"{truncated_output}\n\n"
        f"{rules_prompt.strip()}"
    )


def run_check_hook(hook_name: str, command: str, rules_prompt: str) -> int:
    hook_input = read_hook_input()
    block_count_key = build_block_count_key(hook_input.get("session_id", "?"), hook_name)

    try:
        exit_code, command_output = run_command(command)
    except subprocess.TimeoutExpired:
        trace(f"[{block_count_key}] délai de {COMMAND_TIMEOUT_SEC} s dépassé : on laisse passer")
        return 0

    if exit_code == 0:
        trace(f"[{block_count_key}] OK : compteur remis à zéro")
        reset_block_count(block_count_key)
        return 0

    if not can_block(block_count_key):
        trace(f"[{block_count_key}] limite de {MAX_BLOCK_COUNT} blocages atteinte : on laisse passer")
        return 0

    return block(block_count_key, build_block_message(command, command_output, rules_prompt))
