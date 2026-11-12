"""Two things that went wrong on 2026-11-11, while Alexv's laptop lid was closed:

1. RELOAD. A restart meant to load a loop fix ran the OLD code for 14 hours: the loop loads loop.py from
   the unit's branch, and brings master (with the fix) into that branch only afterwards. Now, between
   phases and between units, a loop whose own file changed on disk saves its state and restarts itself.
2. SLEEP. A session frozen by Windows standby at 13:32 was killed for "running 2 h" when the PC woke at
   23:31. Now only awake time counts against a session's time limit.

Run from the project root:  python loop/tests/test_reload_and_sleep.py
Uses the real run_unit/start_branch on a throw-away git repo, the real main(), a real child process for
relaunch(), and a real process for run_process() under a scripted clock.
"""
import importlib.util
import os
import subprocess
import sys
import tempfile
import types
from pathlib import Path

ROOT = Path(os.getcwd())
spec = importlib.util.spec_from_file_location("loop_under_test", ROOT / "loop" / "loop.py")
L = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = L
spec.loader.exec_module(L)

events: list = []
for name in ("report", "log", "notify"):
    setattr(L, name, lambda msg, *a, _n=name, **k: events.append((_n, msg)))
saved = []
L.save_state = lambda state: saved.append(1)
L.push = lambda *refs: None
L.sync_master = lambda: None
L.ensure_unity = lambda: None
L.budget_pause = lambda state: False
L.keep_awake = lambda on=True: None

bad = 0


def check(label, ok, detail):
    global bad
    bad += not ok
    print("ok  " if ok else "FAIL", label, "->", detail)


def repo():
    d = Path(tempfile.mkdtemp(prefix="loop-reload-"))
    run = lambda *a: subprocess.run(["git", *a], cwd=d, capture_output=True, text=True, check=True,
                                    stdin=subprocess.DEVNULL)
    run("init", "-q", "-b", "master")
    run("config", "user.email", "loop@test"), run("config", "user.name", "loop test")
    (d / "loop").mkdir()
    (d / "loop" / "loop.py").write_text("# loop v1\n")
    (d / ".gitignore").write_text("loop/runtime/\n")
    run("add", "."), run("commit", "-q", "-m", "base")
    L.ROOT = d
    L.LOGS = d / "loop" / "runtime" / "logs"
    L.RESULTS = d / "loop" / "runtime" / "results"
    L.IMPL = d / "_bmad-output" / "implementation-artifacts"
    L.LOGS.mkdir(parents=True)
    L.STOP_FILE = d / "STOP-never"
    L.CODE_FILE = d / "loop" / "loop.py"
    L.CODE_DIGEST = L.code_digest(L.CODE_FILE)        # what this "process" loaded: v1
    return d, run


def new_state(phase="close"):
    return {"units": {"x": {"kind": "story", "epic": 1, "phase": phase, "attempts": {}, "parked": None,
                            "merged": False, "base_ref": None, "decisions": [], "human_checks": [],
                            "lessons": [], "history": [], "findings": None, "hint": ""}}}


# ---------------------------------------------------------------- 1. reload, the 2026-11-11 sequence
d, run = repo()
run("checkout", "-q", "-b", "loop/x"), (d / "work.txt").write_text("story work\n")
run("add", "."), run("commit", "-q", "-m", "story work")
run("checkout", "-q", "master"), (d / "loop" / "loop.py").write_text("# loop v2: the fix\n")
run("commit", "-q", "-am", "loop fix on master")
run("checkout", "-q", "loop/x")        # the loop was stopped on the unit's branch, then started: loads v1

sessions = []
L.run_phase = lambda u, us, phase, state: sessions.append(phase) or {"status": "stopped"}
state = new_state()
outcome = L.run_unit(L.Unit("story", "x", 1), state)
check("resuming a branch that brings in new loop code returns 'reload' before any session runs",
      outcome == "reload" and sessions == [], f"{outcome}, sessions {sessions}")
check("...the branch now has master's loop code on disk",
      (d / "loop" / "loop.py").read_text().strip() == "# loop v2: the fix", (d / "loop" / "loop.py").read_text().strip())
check("...and the state was saved first (base_ref re-based on the new merge-base)",
      saved and state["units"]["x"]["base_ref"] == run("rev-parse", "master").stdout.strip(),
      state["units"]["x"]["base_ref"])

L.CODE_DIGEST = L.code_digest(L.CODE_FILE)   # the relaunched process loads v2
outcome = L.run_unit(L.Unit("story", "x", 1), state)
check("the relaunched process (now on the new code) runs the phase as normal",
      outcome == "stopped" and sessions == ["close"], f"{outcome}, sessions {sessions}")

# ---------------------------------------------------------------- 2. no change, no reload
d, run = repo()
sessions.clear()
outcome = L.run_unit(L.Unit("story", "x", 1), new_state())
check("unchanged loop code: no reload, the phase runs", outcome == "stopped" and sessions == ["close"],
      f"{outcome}, sessions {sessions}")

# ---------------------------------------------------------------- 3. main(): between units, and on 'reload'
def drive_main(outcomes, change_after_first):
    d, run = repo()
    calls = []

    def fake_run_unit(u, state):
        calls.append(u.id)
        if change_after_first and len(calls) == 1:
            (d / "loop" / "loop.py").write_text("# loop v3\n")   # e.g. the next unit's checkout of master
        return outcomes[min(len(calls) - 1, len(outcomes) - 1)]

    L.run_unit = fake_run_unit
    L.load_state = lambda: new_state()
    L.config = lambda: {}
    L.load_sprint = L.load_deps = lambda: None
    L.pick_next = lambda *a, **k: L.Unit("story", f"u{len(calls) + 1}", 1)
    old_argv, sys.argv = sys.argv, ["loop.py"]
    L.RUNTIME, L.REPORTS = d / "loop" / "runtime", d / "loop" / "runtime" / "reports"
    L.CONFIG_FILE = L.RUNTIME / "config.local.json"
    try:
        code = L.main()
    finally:
        sys.argv = old_argv
    return code, calls


real_run_unit = L.run_unit
code, calls = drive_main(["merged"], change_after_first=True)
check("main(): new loop code on disk after a unit merges -> RELOAD before picking the next unit",
      code == L.RELOAD and calls == ["u1"], f"code {code}, units run {calls}")
events.clear()
code, calls = drive_main(["reload"], change_after_first=False)
check("main(): a unit that returns 'reload' -> RELOAD", code == L.RELOAD and calls == ["u1"], f"code {code}, {calls}")
check("...and it says why in the log", any("restarting on the new code" in m for n, m in events),
      [m for n, m in events if "new code" in m][:1])
code, calls = drive_main(["merged", "stopped"], change_after_first=False)
check("main(): unchanged code carries on to the next unit", calls == ["u1", "u2"] and code == 0, f"code {code}, {calls}")
L.run_unit = real_run_unit

# ---------------------------------------------------------------- 4. relaunch(): a real child process
d = Path(tempfile.mkdtemp(prefix="loop-relaunch-"))
child = d / "child.py"
child.write_text("import sys, pathlib\npathlib.Path(sys.argv[0]).with_suffix('.args').write_text(' '.join(sys.argv[1:]))\n"
                 "sys.exit(7)\n")
L.CODE_FILE, L.ROOT = child, d
code = L.relaunch(["--only", "2-2-walk-the-whole-town"])
check("relaunch() runs the file on disk with the same arguments and returns its exit code",
      code == 7 and (d / "child.args").read_text() == "--only 2-2-walk-the-whole-town",
      f"exit {code}, args {(d / 'child.args').read_text() if (d / 'child.args').exists() else None!r}")

# ---------------------------------------------------------------- 5. the time limit counts awake time only
real_time = L.time


def timed_run(timeout_s, jumps):
    """run_process on a real process that would run for 120 s, with a scripted clock: each 10 s poll
    advances 10 s, plus jumps[i] extra seconds at poll i (Windows asleep)."""
    clock = {"t": 1_000_000.0, "polls": 0}

    def fake_sleep(s):
        clock["polls"] += 1
        clock["t"] += s + jumps.get(clock["polls"], 0)

    L.time = types.SimpleNamespace(time=lambda: clock["t"], sleep=fake_sleep)
    d = Path(tempfile.mkdtemp(prefix="loop-sleep-"))
    L.PID_FILE, L.ROOT = d / "current.pid", d
    events.clear()
    try:
        code, _ = L.run_process([sys.executable, "-c", "import time; time.sleep(120)"], "", d / "s.jsonl",
                                timeout_s, dict(os.environ))
    finally:
        L.time = real_time
    return clock["polls"], [m for n, m in events]


polls, log_lines = timed_run(100, {})
check("no standby: killed after ~100 s of awake time", polls == 11 and any("timeout after" in m for m in log_lines),
      f"{polls} polls; {log_lines[-1:]}")
polls, log_lines = timed_run(100, {3: 10 * 3600})
check("10 h asleep at poll 3: not killed on waking; killed after 100 s AWAKE",
      polls == 12 and any("asleep for 600 min" in m for m in log_lines)
      and any("timeout after" in m for m in log_lines), f"{polls} polls; {log_lines}")
polls, log_lines = timed_run(100, {2: 50, 4: 50})
check("a slow poll under the threshold still counts (it is not sleep)",
      polls < 11 and not any("asleep" in m for m in log_lines), f"{polls} polls; {log_lines[-1:]}")

L.ROOT = ROOT
print("FAILURES:", bad)
sys.exit(1 if bad else 0)
