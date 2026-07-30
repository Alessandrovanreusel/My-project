"""PostToolUse hook: turn two CLAUDE.md prose rules into mechanical reminders.

CLAUDE.md states both of these as standing requirements, but prose in CLAUDE.md is a
rule the agent can drift past on a long turn. This fires at the exact moment each rule
becomes relevant instead:

  1. A C# file under Assets/ changed  -> verify it compiled (read_console).
  2. A planning/implementation artifact changed -> mirror it to Jira in the same session.

Design notes:
  * Never blocks. Any failure - bad JSON, missing key, wrong Python - exits 0 silently.
    A hook that breaks the workflow is worse than no hook, and a hook that logs its own
    errors trains you to ignore errors (CLAUDE.md, "Traps already paid for").
  * No third-party imports, so it cannot break on a dependency.
"""

import json
import re
import sys

ASSETS_CS = re.compile(r"(^|/)Assets/.*\.cs$", re.IGNORECASE)
ARTIFACT = re.compile(
    r"(^|/)_bmad-output/(planning-artifacts|implementation-artifacts)/.*\.(md|ya?ml)$",
    re.IGNORECASE,
)

COMPILE_MSG = (
    "A C# file under Assets/ just changed. Per CLAUDE.md, a clean compile is the minimum "
    "bar before saying anything works: call mcp__UnityMCP__read_console (types: error, "
    "warning) to confirm it compiled. Remember that a clean compile only proves it parses "
    "- it is not evidence the feature works."
)

JIRA_MSG = (
    "A BMad planning/implementation artifact just changed. Per CLAUDE.md 'Jira Sync', the "
    "file and Jira are one unit of work: reflect this change in Jira project KAN before "
    "ending the turn, read the jiraSync: block in epics.md first so you update rather than "
    "duplicate, and write any new keys back into that block."
)


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return 0

    try:
        path = str((payload.get("tool_input") or {}).get("file_path") or "")
    except Exception:
        return 0

    if not path:
        return 0

    path = path.replace("\\", "/")

    notes = []
    if ASSETS_CS.search(path):
        notes.append(COMPILE_MSG)
    if ARTIFACT.search(path):
        notes.append(JIRA_MSG)

    if not notes:
        return 0

    try:
        json.dump(
            {
                "hookSpecificOutput": {
                    "hookEventName": "PostToolUse",
                    "additionalContext": " ".join(notes),
                }
            },
            sys.stdout,
        )
    except Exception:
        return 0
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:
        sys.exit(0)
