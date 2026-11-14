"""Rebuild AGENTS.md (the instructions Codex reads) from CLAUDE.md (the source of truth).

    python tools/sync_agents_md.py           rebuild AGENTS.md
    python tools/sync_agents_md.py --check   only compare: exit 1 if AGENTS.md is stale

Why a script: AGENTS.md used to be a hand-made copy of CLAUDE.md, and nobody updated it when CLAUDE.md
changed. By 2026-11-15 it had silently lost the loop rules, the toolbox, the MCP version pins and a trap
that had cost two rig runs. Generating it means the two files can only differ where Codex really works
differently from Claude Code.

How: AGENTS.md is CLAUDE.md with the swaps below applied. Each swap quotes the exact CLAUDE.md passage it
replaces, and the script stops with an error when that passage is missing or appears twice, instead of
writing a copy that quietly lacks the Codex version. So if you reword one of those passages in CLAUDE.md,
update its swap here to match, then run the script again.

Standard library only, so it runs on any Python 3.
"""

import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / "CLAUDE.md"
TARGET = ROOT / "AGENTS.md"

# (passage in CLAUDE.md, what Codex reads instead), in file order.
SWAPS = [
    # 1. Who the file is for, and that it is generated.
    ("""\
# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

`AGENTS.md` is Codex's copy of this file, generated from it. After changing this file, run
`python tools/sync_agents_md.py` and commit the rebuilt `AGENTS.md` with it.
""", """\
# AGENTS.md

This file provides guidance to Codex when working with code in this repository.

**It is generated from `CLAUDE.md` by `tools/sync_agents_md.py`, so do not edit it by hand.** `CLAUDE.md` is
the source of truth: to change an instruction, change `CLAUDE.md`, run the script and commit both files.
Everything below matches `CLAUDE.md` except where Codex works differently from Claude Code: how you reach
the skills, the project memory, one MCP tip and the loop's hook.
"""),
    # 2. Which agent talks to the editor.
    ("enabling direct Unity Editor interaction from Claude Code.",
     "enabling direct Unity Editor interaction from Codex."),
    # 3. An MCP tip for Codex, then the project memory, which Codex does not load on its own.
    ("""\
  grep it to count events across a whole session.

## Toolbox""", """\
  grep it to count events across a whole session.
- If reading an `mcpforunity://` resource fails with an unknown-server error, list the resources first and
  use the server key exactly as it comes back (MCP for Unity's own instructions give this rule for Codex).

## Project memory — Codex does not load it for you

Claude Code sessions keep what they have learned outside the repository, in
`~/.claude/projects/C--Users-alexv-OneDrive-Bureau-Unity-My-project/memory/`: the "project memory" this file
mentions. You never see it unless you open it. Read `MEMORY.md` there (one line per memory), then every file
that touches your task, and before any editor work at least `unity-mcp-update-traps.md` and
`mcp-playmode-verification-gotchas.md`. Treat it as read-only and tell Alexv what you learned instead. Some
entries are permissions Alexv gave Claude sessions (pushing after every change, for example), so ask him
before assuming one covers you.

## Toolbox"""),
    # 4. Skills are Claude Code plugins: Codex reads their SKILL.md files instead of invoking them.
    ("""\
**Skills** (the Claude Code plugins `unity` and `unity-perf`, user scope). Invoke the one that matches the
area you are working in, before writing code there:
""", """\
**Skills** (the Claude Code plugins `unity` and `unity-perf`, user scope). Codex cannot invoke them, but each
skill is a Markdown file you can read: `unity:urp-postprocessing` is
`~/.claude/plugins/cache/anthropic-plugin-directory/unity/<version>/skills/urp-postprocessing/SKILL.md`, and
the `unity-perf:` skills sit under `.../unity-perf/<version>/skills/` the same way. Each plugin has one
version folder, renamed when the plugin updates. Read the skill that matches the area you are working in,
and the files it points to, before writing code there:
"""),
    # 5. The loop runs Claude Code, and its guard hook is a Claude Code hook.
    ("""\
Read `loop/README.md` before touching it. In an interactive session:
""", """\
The loop runs Claude Code sessions, never Codex. Read `loop/README.md` before touching it. In an interactive
session, yours included:
"""),
    ("- `.claude/hooks/loop_guard.py` only acts when `CAMERA_LOOP=1`, so it never affects you here.\n",
     "- `.claude/hooks/loop_guard.py` is a Claude Code hook that only acts when `CAMERA_LOOP=1`: it never"
     " affects you.\n"),
]


def build(claude_md: str) -> str:
    text = claude_md.replace("\r\n", "\n")
    for number, (passage, codex) in enumerate(SWAPS, 1):
        found = text.count(passage)
        if found != 1:
            sys.exit(
                f"swap {number}: this CLAUDE.md passage should appear exactly once, found {found}:\n\n"
                f"{passage}\n\nUpdate swap {number} in tools/sync_agents_md.py to match CLAUDE.md, then run "
                "it again. AGENTS.md was not touched."
            )
        text = text.replace(passage, codex)
    return text


def main() -> int:
    raw = SOURCE.read_bytes().decode("utf-8")
    newline = "\r\n" if "\r\n" in raw else "\n"  # keep the line endings this checkout uses
    wanted = build(raw)
    current = TARGET.read_bytes().decode("utf-8").replace("\r\n", "\n") if TARGET.exists() else None

    if "--check" in sys.argv[1:]:
        if current == wanted:
            print("AGENTS.md is up to date with CLAUDE.md")
            return 0
        print("AGENTS.md is stale: run  python tools/sync_agents_md.py")
        return 1

    if current == wanted:
        print("AGENTS.md was already up to date")
        return 0
    TARGET.write_bytes(wanted.replace("\n", newline).encode("utf-8"))
    print("AGENTS.md rebuilt from CLAUDE.md")
    return 0


if __name__ == "__main__":
    sys.exit(main())
