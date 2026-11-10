"""PreToolUse seatbelt for the unattended loop (loop/loop.py).

The loop runs `claude -p --dangerously-skip-permissions`, so nothing prompts. Hooks still run in
that mode, and a PreToolUse "deny" still blocks the call. This file is the last line of defence
against the few actions an unattended agent must never take, because they cannot be undone or
because they reach outside the project:

  * rewriting or destroying git history, and branch management (that is the orchestrator's job);
  * sweeping unrelated files into a commit (`git add -A` / `.` / `commit -a`);
  * recursive deletes outside the project, or of the project's top-level folders;
  * machine-level changes (format, registry, power, execution policy);
  * editing the agent's own guard rails (.claude settings and hooks, ~/.claude.json), or approving
    its own gated work (loop/runtime/approvals.txt is Alexv's);
  * outward-facing account actions: e-mail, Drive, calendar and other claude.ai connectors;
    artifact publishing; uploading local files or visiting checkout/billing pages in Chrome.

It is a seatbelt, not a jail: a determined agent could still find a way around a pattern list.
Everything else is allowed, on purpose.

Active ONLY when the environment variable CAMERA_LOOP=1, which loop.py sets for its sessions.
Interactive sessions are untouched. It never blocks on its own failure: any error exits 0 silently,
because a guard that crashes the workflow is worse than no guard.
"""

import json
import os
import re
import sys

ROOT = os.path.normcase(os.path.abspath(os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd()))

SHELL_TOOLS = {"Bash", "PowerShell"}
WRITE_TOOLS = {"Edit", "Write", "NotebookEdit", "MultiEdit"}

# Tools the loop never needs and that act on Alexv's accounts or publish things.
DENY_TOOL_PREFIXES = ("mcp__claude_ai_",)
DENY_TOOLS = {
    "Artifact", "ArtifactComments", "ArtifactData", "SendToProject", "SendToProjectContext",
    "RemoteTrigger", "CronCreate", "CronDelete", "Workflow", "AskUserQuestion",
    "mcp__claude-in-chrome__file_upload",
}

GIT_RULES = [
    (r"\bgit\b[^\n|;&]*\bpush\b[^\n|;&]*(--force\b|--force-with-lease|\s-f\b|\s\+\S)", "force-push"),
    (r"\bgit\b[^\n|;&]*\bpush\b[^\n|;&]*(--delete\b|\s:\S)", "deleting a remote branch"),
    (r"\bgit\b[^\n|;&]*\bpush\b", "pushing (the orchestrator pushes)"),
    (r"\bgit\b[^\n|;&]*\breset\b[^\n|;&]*--hard", "git reset --hard (use git restore <file> or git revert)"),
    (r"\bgit\b[^\n|;&]*\bclean\b", "git clean (it deletes untracked files you did not create)"),
    (r"\bgit\b[^\n|;&]*\b(rebase|merge(?!-base)|cherry-pick|filter-branch|filter-repo|replace|update-ref)\b", "history/branch surgery (the orchestrator merges)"),
    (r"\bgit\b[^\n|;&]*\b(switch|worktree)\b", "switching branches (stay on your story branch)"),
    (r"\bgit\b[^\n|;&]*\bcheckout\b(?![^\n|;&]*\s--\s)", "git checkout of a branch (use `git checkout -- <file>` or `git restore`)"),
    (r"\bgit\b[^\n|;&]*\bbranch\b[^\n|;&]*\s-(d|D|m|M|f)\b", "deleting/renaming branches"),
    (r"\bgit\b[^\n|;&]*\btag\b", "tagging (the orchestrator tags)"),
    (r"\bgit\b[^\n|;&]*\badd\b[^\n|;&]*(\s-A\b|\s--all\b|\s\.(\s|$)|\s\*(\s|$)|\s-u\b)", "bulk staging (stage files by name)"),
    (r"\bgit\b[^\n|;&]*\bcommit\b[^\n|;&]*\s-[a-zA-Z]*a", "git commit -a (stage files by name)"),
    (r"\bgit\b[^\n|;&]*\bstash\b", "git stash (it hides work from the orchestrator)"),
]

MACHINE_RULES = [
    (r"\b(format(-volume)?\s+[a-z]:|diskpart|bcdedit|vssadmin)\b", "disk/boot tooling"),
    (r"\breg(\.exe)?\s+(add|delete|import)\b|Remove-ItemProperty|Set-ItemProperty\s+-Path\s+'?HK", "registry edits"),
    (r"\b(shutdown(\.exe)?|Stop-Computer|Restart-Computer|powercfg)\b", "power state"),
    (r"Set-ExecutionPolicy", "execution policy"),
    (r"\b(Uninstall-Package|winget\s+uninstall|choco\s+uninstall)\b", "uninstalling software"),
]

GUARDED_PATHS = re.compile(r"(\.claude[/\\](settings|hooks)|\.claude\.json|\.codex[/\\](config|hooks))", re.I)
APPROVALS = re.compile(r"approvals\.txt", re.I)   # Alexv's sign-offs: a loop session must never approve itself
WRITEY = re.compile(r"(>|\bSet-Content\b|\bAdd-Content\b|\bOut-File\b|\bCopy-Item\b|\bMove-Item\b|\bRemove-Item\b|"
                    r"\brm\b|\bmv\b|\bcp\b|\bdel\b|\bsed\s+-i|\btee\b|\bNew-Item\b|\bpython\b|\bnode\b)", re.I)
RECURSIVE_DELETE = re.compile(r"(\brm\s+(-[a-zA-Z]*[rR][a-zA-Z]*|--recursive)|Remove-Item[^\n|;]*-Recurse|\brd\s+/s|\brmdir\s+/s|\bdel\s+/s|shutil\.rmtree)", re.I)
PROTECTED_TOP = re.compile(r"(^|[\s'\"/\\])(Assets|ProjectSettings|Packages|\.git|_bmad|_bmad-output|loop|tools|\.claude)([/\\]?)(['\"\s]|$)", re.I)
CHROME_BAD_URL = re.compile(r"(checkout|payment|billing|purchase|subscribe|/buy\b|/cart\b|/order|paypal|stripe\.com|account/(security|delete))", re.I)


def deny(reason):
    json.dump({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": "LOOP GUARD blocked this: " + reason + ". "
            "If it is genuinely required, record it as a blocker in your result file instead.",
    }}, sys.stdout)
    return 0


def outside_root(path):
    try:
        p = os.path.normcase(os.path.abspath(os.path.expanduser(path)))
    except Exception:
        return False
    return not (p == ROOT or p.startswith(ROOT + os.sep))


def check_shell(cmd):
    for pattern, why in GIT_RULES + MACHINE_RULES:
        if re.search(pattern, cmd, re.I):
            return why
    if GUARDED_PATHS.search(cmd) and WRITEY.search(cmd):
        return "changing the agent's own settings or hooks"
    if APPROVALS.search(cmd) and WRITEY.search(cmd):
        return "touching loop/runtime/approvals.txt from a shell (only Alexv approves; read it with the Read tool)"
    if RECURSIVE_DELETE.search(cmd):
        if re.search(r"(~|\$HOME|\$env:USERPROFILE|%USERPROFILE%|\.\.[/\\]|[A-Za-z]:[/\\](?!.*My project)|(^|[\s'\"])/[a-zA-Z]/(?!.*My project))", cmd, re.I):
            return "recursive delete reaching outside the project"
        if PROTECTED_TOP.search(cmd):
            return "recursive delete of a top-level project folder"
    return None


def main():
    if os.environ.get("CAMERA_LOOP") != "1":
        return 0
    try:
        payload = json.load(sys.stdin)
    except Exception:
        return 0
    tool = str(payload.get("tool_name") or "")
    inp = payload.get("tool_input") or {}

    if tool in DENY_TOOLS or tool.startswith(DENY_TOOL_PREFIXES):
        return deny(f"the tool {tool} (outward-facing or not needed by the loop)")

    # The cross-review session is a READ-ONLY second opinion: it may read and run git diff/log/show,
    # never change the tree. (loop.py also switches its editing tools off and reverts tracked changes.)
    if os.environ.get("LOOP_PHASE") == "cross-review":
        if tool in WRITE_TOOLS:
            return deny("editing files during the read-only cross-review (report it as a finding instead)")
        if tool in SHELL_TOOLS and re.search(r"\bgit\b[^\n|;&]*\b(add|commit|restore|rm|mv|apply|am|revert)\b",
                                             str(inp.get("command") or ""), re.I):
            return deny("changing the repository during the read-only cross-review")

    if tool in SHELL_TOOLS:
        why = check_shell(str(inp.get("command") or ""))
        if why:
            return deny(why)

    if tool in WRITE_TOOLS:
        path = str(inp.get("file_path") or inp.get("notebook_path") or "")
        if GUARDED_PATHS.search(path.replace("\\", "/")):
            return deny("editing the agent's own settings or hooks")
        if APPROVALS.search(path):
            return deny("writing loop/runtime/approvals.txt (only Alexv approves gated work)")
        if path and outside_root(path) and "\\Temp\\claude\\" not in path and "/Temp/claude/" not in path:
            if not re.search(r"[/\\]\.claude[/\\]projects[/\\][^/\\]*My-project[/\\]memory[/\\]", path, re.I):
                return deny(f"writing outside the project ({path})")

    if tool == "mcp__claude-in-chrome__navigate":
        url = str(inp.get("url") or "")
        if CHROME_BAD_URL.search(url):
            return deny(f"visiting a payment/account page ({url})")

    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:
        sys.exit(0)
