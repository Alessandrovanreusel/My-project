"""Exercise .claude/hooks/loop_guard.py the way Claude Code calls it: JSON on stdin, decision on stdout.

Run from the project root:  python loop/tests/test_loop_guard.py
Prints one line per case and exits 1 if any case disagrees with its expectation.
"""
import json
import os
import subprocess
import sys

ROOT = os.getcwd()
HOOK = os.path.join(ROOT, ".claude", "hooks", "loop_guard.py")


def run(tool, inp, loop=True, phase="dev"):
    env = dict(os.environ)
    env["CLAUDE_PROJECT_DIR"] = ROOT
    env["LOOP_PHASE"] = phase
    if loop:
        env["CAMERA_LOOP"] = "1"
    else:
        env.pop("CAMERA_LOOP", None)
    p = subprocess.run([sys.executable, HOOK], input=json.dumps({"tool_name": tool, "tool_input": inp}),
                       capture_output=True, text=True, env=env)
    return "DENY" if '"deny"' in p.stdout else "allow"


B, PS = "Bash", "PowerShell"
CASES = [
    ("DENY", B, {"command": "git push --force origin master"}, "force push"),
    ("DENY", B, {"command": "git push origin loop/1-14"}, "any push (orchestrator pushes)"),
    ("DENY", B, {"command": "git add -A"}, "add -A"),
    ("DENY", B, {"command": "git add ."}, "add ."),
    ("DENY", B, {"command": "git commit -am 'x'"}, "commit -am"),
    ("DENY", B, {"command": "git reset --hard HEAD~1"}, "reset --hard"),
    ("DENY", B, {"command": "git clean -fd"}, "clean"),
    ("DENY", B, {"command": "git checkout master"}, "checkout a branch"),
    ("DENY", B, {"command": "git merge loop/x"}, "merge"),
    ("DENY", B, {"command": "git stash"}, "stash"),
    ("DENY", B, {"command": "rm -rf /c/Users/alexv/Documents"}, "rm -rf outside (git-bash path)"),
    ("DENY", PS, {"command": r"Remove-Item -Recurse -Force C:\Users\alexv\Documents"}, "Remove-Item -Recurse outside"),
    ("DENY", B, {"command": "rm -rf Assets"}, "rm -rf top-level Assets"),
    ("DENY", B, {"command": "echo x > .claude/settings.json"}, "write own settings via shell"),
    ("DENY", PS, {"command": "shutdown /s /t 0"}, "power state"),
    ("DENY", "Edit", {"file_path": os.path.join(ROOT, ".claude", "hooks", "loop_guard.py")}, "edit own guard"),
    ("DENY", "Write", {"file_path": r"C:\Users\alexv\Desktop\x.txt"}, "write outside project"),
    ("DENY", "mcp__claude_ai_Gmail__send_message", {}, "gmail send"),
    ("DENY", "Artifact", {}, "publish artifact"),
    ("DENY", "mcp__claude-in-chrome__navigate", {"url": "https://store.unity.com/checkout"}, "chrome checkout page"),
    ("DENY", "mcp__claude-in-chrome__file_upload", {}, "chrome file upload"),
    ("allow", B, {"command": "git add Assets/Scripts/Recording/ClipRecorder.cs Assets/Scripts/Recording/ClipRecorder.cs.meta"}, "add by name"),
    ("allow", B, {"command": 'git commit -m "Record a clip: add ClipRecorder"'}, "commit -m"),
    ("allow", B, {"command": "git diff $(git merge-base master HEAD)..HEAD --stat"}, "merge-base diff"),
    ("allow", B, {"command": "git checkout -- Assets/Scenes/SampleScene.unity"}, "checkout -- file"),
    ("allow", B, {"command": "git log --oneline --tags -5"}, "log --tags"),
    ("allow", B, {"command": "rm -rf _bmad-output/verification/1-14/old"}, "rm inside evidence dir"),
    ("allow", "Write", {"file_path": os.path.join(ROOT, "Assets", "Scripts", "Recording", "ClipRecorder.cs")}, "write in project"),
    ("allow", "Write", {"file_path": r"C:\Users\alexv\AppData\Local\Temp\claude\x\scratch.py"}, "write scratchpad"),
    ("allow", "mcp__claude-in-chrome__navigate", {"url": "https://docs.unity3d.com/Manual/index.html"}, "chrome docs"),
    ("allow", "mcp__UnityMCP__read_console", {}, "unity mcp"),
]

bad = 0
for expected, tool, inp, label in CASES:
    got = run(tool, inp)
    ok = got == expected
    bad += not ok
    print(("ok  " if ok else "FAIL"), f"expected {expected:5} got {got:5}  {label}")
READ_ONLY_CASES = [   # the cross-review phase may look, never touch
    ("DENY", "Edit", {"file_path": os.path.join(ROOT, "Assets", "Scripts", "X.cs")}, "cross-review: edit a file"),
    ("DENY", "Write", {"file_path": os.path.join(ROOT, "notes.md")}, "cross-review: write a file"),
    ("DENY", B, {"command": "git commit -m 'review notes'"}, "cross-review: commit"),
    ("DENY", B, {"command": "git restore Assets/Scripts/X.cs"}, "cross-review: restore"),
    ("allow", B, {"command": "git diff abc123..loop/1-14 -- Assets/Scripts"}, "cross-review: git diff"),
    ("allow", B, {"command": "git log --oneline -20"}, "cross-review: git log"),
    ("allow", "Read", {"file_path": os.path.join(ROOT, "Assets", "Scripts", "X.cs")}, "cross-review: read"),
]
for expected, tool, inp, label in READ_ONLY_CASES:
    got = run(tool, inp, phase="cross-review")
    ok = got == expected
    bad += not ok
    print(("ok  " if ok else "FAIL"), f"expected {expected:5} got {got:5}  {label}")
dev_write = run("Write", {"file_path": os.path.join(ROOT, "notes.md")}, phase="dev")
print(("ok  " if dev_write == "allow" else "FAIL"), f"dev phase may still write in the project -> {dev_write}")
bad += dev_write != "allow"

inactive = run(B, {"command": "git push --force"}, loop=False)
print(("ok  " if inactive == "allow" else "FAIL"), f"interactive session (no CAMERA_LOOP), force-push -> {inactive} (guard must stay off)")
bad += inactive != "allow"
print("FAILURES:", bad)
sys.exit(1 if bad else 0)
