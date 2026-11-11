"""A close never certifies its own build: when a close attempt passes after changing the game itself,
a fresh close verifies that change before the merge. Docs, evidence and rigs don't count.

Run from the project root:  python loop/tests/test_close_rebuild.py
Drives the real run_unit() and the real run_phase() (prompt rendering included) on a throw-away git
repo; only the Claude session itself and the merge are scripted.
Written after Story 2.2's first close failed AC3 and handed its retry a plan to build a rock edge round
the whole mountain ring: that retry would have built it, judged it and merged it on its own.
"""
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(os.getcwd())
spec = importlib.util.spec_from_file_location("loop_under_test", ROOT / "loop" / "loop.py")
L = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = L
spec.loader.exec_module(L)

events: list = []
for name in ("report", "log", "notify"):
    setattr(L, name, lambda msg, *a, _n=name, **k: events.append((_n, msg)))
L.save_state = lambda state: None
L.push = lambda *refs: None
L.sync_master = lambda: None
L.ensure_unity = lambda: None
L.budget_pause = lambda state: False

RUNTIME_SCRIPT = "#if UNITY_EDITOR\nusing UnityEngine;\npublic class EdgeRunner : MonoBehaviour {}\n#endif\n"
SHIPPED_RUNNER = "using UnityEngine;\npublic class EdgeRunner : MonoBehaviour {}\n"


def repo():
    d = Path(tempfile.mkdtemp(prefix="loop-reclose-"))
    run = lambda *a: subprocess.run(["git", *a], cwd=d, capture_output=True, text=True, check=True,
                                    stdin=subprocess.DEVNULL)
    run("init", "-q", "-b", "master")
    run("config", "user.email", "loop@test"), run("config", "user.name", "loop test")
    (d / "story.txt").write_text("start\n")
    run("add", "."), run("commit", "-q", "-m", "base")
    L.ROOT = d
    L.LOGS = d / "loop" / "runtime" / "logs"
    L.RESULTS = d / "loop" / "runtime" / "results"
    L.IMPL = d / "_bmad-output" / "implementation-artifacts"
    L.LOGS.mkdir(parents=True)
    (d / ".gitignore").write_text("loop/runtime/\n")
    run("add", "."), run("commit", "-q", "-m", "ignore runtime")
    L.STOP_FILE = d / "STOP-never"
    return d, run


def drive(script):
    """script: one entry per close session: (status, {path: text}) — the files that session commits."""
    d, run = repo()
    sessions, merged = [], []
    queue = list(script)

    def fake_session(u, phase, prompt, logfile):
        status, files = queue.pop(0) if queue else ("ok", {})
        sessions.append({"phase": phase, "prompt": prompt, "status": status})
        for p, text in files.items():
            (d / p).parent.mkdir(parents=True, exist_ok=True)
            (d / p).write_text(text)
        if files:
            run("add", "-A", "--", *files), run("commit", "-q", "-m", f"session {len(sessions)}")
        logfile.write_text("")
        fake_session.last = {"status": status, "summary": f"session {len(sessions)} says {status}",
                             "next_hint": f"build it (session {len(sessions)})"}
        return 0, ""

    L.claude_session = fake_session
    L.read_result = lambda path: dict(fake_session.last)
    L.merge_unit = lambda u, us, state: merged.append(u.id) or True
    events.clear()
    state = {"units": {"x": {"kind": "story", "epic": 1, "phase": "close", "attempts": {}, "parked": None,
                             "merged": False, "base_ref": None, "decisions": [], "human_checks": [],
                             "lessons": [], "history": [], "findings": None, "hint": ""}}}
    outcome = L.run_unit(L.Unit("story", "x", 1), state)
    return outcome, state["units"]["x"], sessions, merged


bad = 0


def check(label, ok, detail):
    global bad
    bad += not ok
    print("ok  " if ok else "FAIL", label, "->", detail)


VERIFY = "### Verify what the previous close built"
DID_NOT_FINISH = "### The previous attempt did not finish"

# 1. Story 2.2's case: a close fails with a build plan, the retry builds and passes, a fresh close verifies.
outcome, us, s, merged = drive([
    ("failed", {"_bmad-output/implementation-artifacts/x.md": "close gate: AC3 fails\n"}),
    ("ok", {"Assets/Scripts/Editor/TownEdgeBuilder.cs": "class B {}\n", "Assets/Scenes/SampleScene.unity": "edge\n"}),
    ("ok", {"_bmad-output/implementation-artifacts/x.md": "verified\n"}),
])
check("a retry that builds and passes is verified by a fresh close, then merged",
      outcome == "merged" and len(s) == 3 and merged == ["x"], f"{outcome}, {len(s)} closes, merged {merged}")
check("...the verifying close is told what was built, under its own heading",
      VERIFY in s[2]["prompt"] and "Assets/Scenes/SampleScene.unity" in s[2]["prompt"]
      and "TownEdgeBuilder.cs" in s[2]["prompt"] and DID_NOT_FINISH not in s[2]["prompt"],
      [l for l in s[2]["prompt"].splitlines() if "changed the game" in l][:1])
check("...and the builder's own attempt still got the failed close's plan",
      DID_NOT_FINISH in s[1]["prompt"] and "build it (session 1)" in s[1]["prompt"], "hint present")
check("...the log says why another close runs",
      any("a fresh close verifies them" in m for n, m in events), [m for n, m in events if "fresh close" in m][:1])

# 2. A close that only writes docs and evidence merges straight away.
outcome, us, s, merged = drive([("ok", {"_bmad-output/implementation-artifacts/x.md": "done\n",
                                         "_bmad-output/implementation-artifacts/sprint-status.yaml": "x: review\n"})])
check("a close that only touches docs merges after one session", outcome == "merged" and len(s) == 1, f"{outcome}, {len(s)}")

# 3. A close that commits nothing merges straight away.
outcome, us, s, merged = drive([("ok", {})])
check("a close that commits nothing merges after one session", outcome == "merged" and len(s) == 1, f"{outcome}, {len(s)}")

# 4. Rigs (and their .meta) are verification tools, not the game.
outcome, us, s, merged = drive([("ok", {"Assets/Scripts/Editor/TownEdgeRig.cs": "class R {}\n",
                                         "Assets/Scripts/Editor/TownEdgeRig.cs.meta": "guid: 1\n",
                                         "Assets/Scripts/World/EdgeRunner.cs": RUNTIME_SCRIPT,
                                         "Assets/Scripts/World/EdgeRunner.cs.meta": "guid: 2\n"})])
check("a close that only re-tunes rigs merges after one session", outcome == "merged" and len(s) == 1, f"{outcome}, {len(s)}")

# 5. ...but a "Runner" that ships in the build is game code.
outcome, us, s, merged = drive([("ok", {"Assets/Scripts/World/EdgeRunner.cs": SHIPPED_RUNNER}), ("ok", {})])
check("a Runner without #if UNITY_EDITOR ships, so it counts as a build", outcome == "merged" and len(s) == 2,
      f"{outcome}, {len(s)}")

# 6. Project settings and packages are the game too.
outcome, us, s, merged = drive([("ok", {"ProjectSettings/TagManager.asset": "layers\n"}), ("ok", {})])
check("a project-settings change gets a verifying close", outcome == "merged" and len(s) == 2, f"{outcome}, {len(s)}")

# 7. A close that keeps building instead of verifying cannot run forever.
outcome, us, s, merged = drive([("ok", {"Assets/a.txt": f"v{i}\n"}) for i in range(20)])
check("a close that builds every time is parked at MAX_RUNS, never merged",
      outcome == "parked" and len(s) == L.MAX_RUNS and not merged
      and "instead of only verifying" in (us["parked"] or {}).get("reason", ""),
      f"{outcome} after {len(s)}; {(us['parked'] or {}).get('reason', '')[:80]}")

# 8. A verifying close that fixes one small thing gets one more look, then merges.
outcome, us, s, merged = drive([("ok", {"Assets/a.txt": "edge\n"}), ("ok", {"Assets/a.txt": "edge, gap closed\n"}),
                                ("ok", {})])
check("each build gets its own fresh look; the chain ends at the first close that changes nothing",
      outcome == "merged" and len(s) == 3, f"{outcome}, {len(s)}")

L.ROOT = ROOT
print("FAILURES:", bad)
sys.exit(1 if bad else 0)
