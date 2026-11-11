"""Cross-review coverage check: a verdict from a reviewer that never saw the code is re-asked, then marked partial.

Run from the project root:  python loop/tests/test_review_coverage.py
Drives the real run_cross_review() with a scripted stand-in for the Claude session, against Story 1.14's
real commits (fixed SHAs, so the changed-file list never moves). Runtime files go to a temp folder.
Also re-measures the real shallow review of 2026-11-10 if its log is still on this PC.
"""
import importlib.util
import json
import os
import sys
import tempfile
from pathlib import Path

ROOT = Path(os.getcwd())
spec = importlib.util.spec_from_file_location("loop_under_test", ROOT / "loop" / "loop.py")
L = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = L
spec.loader.exec_module(L)

BASE, TIP = "a3a528d", "6418476"   # Story 1.14: base, and its code-review commit
tmp = Path(tempfile.mkdtemp(prefix="loop-review-"))
for name in ("RUNTIME", "LOGS", "RESULTS", "REPORTS"):
    setattr(L, name, tmp / name.lower())
    (tmp / name.lower()).mkdir(parents=True, exist_ok=True)
_rel = L.rel
L.rel = lambda p: _rel(p) if Path(p).is_relative_to(L.ROOT) else str(p)
reports: list = []
L.report = lambda msg: reports.append(msg)

changed = list(L.review_sources(BASE, TIP))


class FixedUnit(L.Unit):
    """A unit whose 'branch' is the fixed 1.14 commit, so the test never depends on a live branch."""
    @property
    def branch(self):
        return TIP


def session_log(files_shown, answer="FINDINGS: 0"):
    lines = [json.dumps({"type": "system", "subtype": "init"})]
    for p in files_shown:   # what a Read of the whole file returns: numbered lines
        body = "\n".join(f"{i + 1}\t{l}" for i, l in enumerate(L.git("show", f"{TIP}:{p}").stdout.splitlines()))
        lines.append(json.dumps({"type": "user", "message": {"content": [{"type": "tool_result", "content": body}]}}))
    lines.append(json.dumps({"type": "result", "is_error": False, "result": answer}))
    return "\n".join(lines) + "\n"


def run(script):
    """script: one list of files per attempt the fake reviewer will 'read'."""
    calls = []

    def fake_run_process(cmd, prompt, logfile, timeout, env):
        files = script[min(len(calls), len(script) - 1)]
        calls.append({"cmd": cmd, "prompt": prompt})
        Path(logfile).write_text(session_log(files), encoding="utf-8")
        return 0, ""

    L.run_process = fake_run_process
    reports.clear()
    us = {"base_ref": BASE}
    path = L.run_cross_review(FixedUnit("story", "1-14-test", 1), us)
    return calls, us, (Path(path).read_text(encoding="utf-8") if path else "")


bad = 0


def check(label, ok, detail):
    global bad
    bad += not ok
    print("ok  " if ok else "FAIL", label, "->", detail)


print(f"Story 1.14 changed source files under review: {len(changed)}")
check("rig exclusions and file filter leave the spike's sources", len(changed) >= 8, changed)

calls, us, text = run([changed])
check("thorough first time: one session, effort flag passed", len(calls) == 1 and "--effort" in calls[0]["cmd"],
      f"sessions={len(calls)} cmd has effort={'--effort' in calls[0]['cmd']}")
check("thorough: no leftover placeholder, coverage recorded", "{coverage_hint}" not in calls[0]["prompt"]
      and f"read {len(changed)}/{len(changed)}" in text, text.strip().splitlines()[-1])

calls, us, text = run([changed[:3], changed])
hint = calls[1]["prompt"] if len(calls) > 1 else ""
check("shallow then thorough: asked again, naming the unread files", len(calls) == 2 and all(p in hint for p in changed[3:])
      and changed[0] not in hint.split("not accepted")[-1].split("## What to review")[0], f"sessions={len(calls)}")
check("shallow then thorough: accepted without a warning", "COVERAGE WARNING" not in text and
      f"read {len(changed)}/{len(changed)}" in text, text.strip().splitlines()[-1])

calls, us, text = run([changed[:2], changed[:3]])
check("shallow twice: review kept but marked partial", len(calls) == 2 and text.startswith("COVERAGE WARNING")
      and "FINDINGS: 0" in text, text.splitlines()[0][:110])
check("shallow twice: Alexv's report says so", any("marked partial" in r for r in reports), [r[:80] for r in reports])

guarded = "#if UNITY_EDITOR\nusing UnityEngine;\n"
check("rigs: editor-folder rigs and editor-guarded runtime runners/feeds are tools",
      L.is_rig("Assets/Scripts/Editor/PhotoShootRig.cs", "using UnityEditor;")
      and L.is_rig("Assets/Scripts/Player/ScaleStudyRunner.cs", "\ufeff" + guarded)
      and L.is_rig("Assets/Scripts/Core/RigVideoFeed.cs", guarded)
      and L.is_rig("Assets/Scripts/World/TownRigRunner.Edges.cs", guarded), "")
check("rigs: a shipped Runner, an unguarded one, or a Rigidbody script is still reviewed",
      not L.is_rig("Assets/Scripts/Events/EventRunner.cs", "using UnityEngine;\n#if UNITY_EDITOR\n")
      and not L.is_rig("Assets/Scripts/Player/RigidbodyMover.cs", guarded), "")


def tool_log(body):
    return json.dumps({"type": "user", "message": {"content": [{"type": "tool_result", "content": body}]}}) + "\n"


diffs = "\n".join(L.git("diff", f"{BASE}..{TIP}", "--", p).stdout for p in changed)   # hunks only, never whole files
read, unread = L.review_coverage(BASE, TIP, tool_log(diffs))
check("a reviewer shown every file's diff (not the whole files) counts as having seen them",
      not unread, f"read {len(read)}/{len(changed)}; unread {unread}")
read, unread = L.review_coverage(BASE, TIP, tool_log(diffs[:30000]))   # a tool limit cut the rest off: never seen
check("...but a diff cut off by the 30k output limit does not", len(unread) > 0,
      f"diff is {len(diffs)} chars; unread {len(unread)}/{len(changed)}")

logs = Path(r"c:/Users/alexv/OneDrive/Bureau/Unity/My project/loop/runtime/logs")
real = logs / "20261110-221850-1-14-decide-how-the-game-records-video-cross-review.jsonl"
if real.exists():
    read, unread = L.review_coverage(BASE, "ae7ed89", real.read_text(encoding="utf-8"))
    check("the real 2026-11-10 review is caught as shallow", len(read) < L.REVIEW_MIN_COVERAGE * (len(read) + len(unread)),
          f"read {len(read)}/{len(read) + len(unread)}; unread {unread}")
real = logs / "20261111-025723-2-1-settle-the-players-scale-in-the-town-cross-review.jsonl"
if real.exists():   # it diffed four comment-only files and was wrongly re-asked; the rig runners no longer count
    read, unread = L.review_coverage("85382ae", "f7045bf", real.read_text(encoding="utf-8"))
    check("the real 2026-11-11 diff review of Story 2.1 is accepted, rigs left out",
          not unread and not any("Runner" in p for p in read), f"read {len(read)}/{len(read) + len(unread)}; unread {unread}")
print("FAILURES:", bad)
sys.exit(1 if bad else 0)
