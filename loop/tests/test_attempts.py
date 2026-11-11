"""Failed attempts: a session that hands over committed work keeps its unit going; one that gets
nowhere still parks it after MAX_ATTEMPTS, and nothing runs forever (MAX_RUNS).

Run from the project root:  python loop/tests/test_attempts.py
Drives the real run_unit() on a throw-away git repo; only the Claude session (run_phase) is scripted.
Written after Story 2.2's first dev session ran out of context having done 7 of 13 tasks in 12
commits, and was counted towards parking exactly like a session that did nothing.
"""
import importlib.util
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


def repo():
    d = Path(tempfile.mkdtemp(prefix="loop-attempts-"))
    run = lambda *a: subprocess.run(["git", *a], cwd=d, capture_output=True, text=True, check=True,
                                    stdin=subprocess.DEVNULL)
    run("init", "-q", "-b", "master")
    run("config", "user.email", "loop@test"), run("config", "user.name", "loop test")
    (d / "story.txt").write_text("start\n")
    run("add", "."), run("commit", "-q", "-m", "base")
    L.ROOT = d
    L.STOP_FILE = d / "STOP-never"
    return d, run


def drive(script):
    """script: one entry per dev session: 'commit' (hands over committed work), 'edit' (leaves a tracked
    change uncommitted; the loop commits it), 'nothing' (gets nowhere), or 'ok'."""
    d, run = repo()
    calls = []

    def fake_run_phase(u, us, phase, state):
        i = len(calls)
        kind = script[min(i, len(script) - 1)]
        calls.append((phase, kind, us.get("hint", "")))
        if phase != "dev":
            return {"status": "stopped"}          # end the drive once dev is over
        if kind in ("commit", "edit"):
            (d / "story.txt").write_text(f"work {i}\n")
            if kind == "commit":
                run("commit", "-q", "-am", f"session {i}")
        if kind == "ok":
            return {"status": "ok", "summary": "done", "_attempt": i + 1}
        return {"status": "failed", "summary": f"session {i}", "next_hint": f"resume after session {i}",
                "_attempt": i + 1}

    L.run_phase = fake_run_phase
    events.clear()
    state = {"units": {"x": {"kind": "story", "epic": 1, "phase": "dev", "attempts": {}, "parked": None,
                             "merged": False, "base_ref": None, "decisions": [], "human_checks": [],
                             "lessons": [], "history": [], "findings": None, "hint": ""}}}
    outcome = L.run_unit(L.Unit("story", "x", 1), state)
    us = state["units"]["x"]
    return outcome, us, [c for c in calls if c[0] == "dev"]


bad = 0


def check(label, ok, detail):
    global bad
    bad += not ok
    print("ok  " if ok else "FAIL", label, "->", detail)


outcome, us, dev = drive(["nothing"])
check("a session that gets nowhere: parked after MAX_ATTEMPTS", outcome == "parked" and len(dev) == L.MAX_ATTEMPTS,
      f"{outcome} after {len(dev)} sessions; {(us['parked'] or {}).get('reason', '')[:70]}")

outcome, us, dev = drive(["commit", "commit", "commit", "ok"])
check("big story handing over three times, then finishing: never parked", outcome == "stopped"
      and len(dev) == 4 and us["phase"] == "review" and not us["parked"], f"{outcome}, phase {us['phase']}, {len(dev)} sessions")
check("...each fresh session gets the previous one's handover", dev[3][2] == "resume after session 2", repr(dev[3][2]))
check("...the log says it continued on progress", sum("handed over with new commits" in m for n, m in events) == 3,
      [m for n, m in events if "handed over" in m][:1])

outcome, us, dev = drive(["edit"])
check("uncommitted edits the loop commits for it also count as progress (until MAX_RUNS)",
      outcome == "parked" and len(dev) == L.MAX_RUNS and "0 of them with nothing" in us["parked"]["reason"],
      f"{outcome} after {len(dev)}; {(us['parked'] or {}).get('reason', '')[:70]}")

outcome, us, dev = drive(["commit"])
check("progress forever is still capped at MAX_RUNS", outcome == "parked" and len(dev) == L.MAX_RUNS,
      f"{outcome} after {len(dev)} sessions")

outcome, us, dev = drive(["commit", "nothing", "commit", "nothing", "nothing"])
check("stalls are counted across handovers: the third stall parks it", outcome == "parked" and len(dev) == 5
      and us["stalls"]["dev"] == 3, f"{outcome} after {len(dev)}; stalls {us.get('stalls')}")

L.ROOT = ROOT
print("FAILURES:", bad)
sys.exit(1 if bad else 0)
