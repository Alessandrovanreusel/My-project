"""Approval gates (dependencies.yaml `gates`): exercise the real scheduler, queue writer and main loop.

Run from the project root:  python loop/tests/test_gates.py
Uses the real sprint-status.yaml and dependencies.yaml with a fabricated loop state, and points every
runtime file at a temporary folder, so it never touches loop/runtime/ and is safe while the loop runs.
Prints what each scenario produced and exits 1 if any disagrees.
"""
import importlib.util
import os
import sys
import tempfile
from pathlib import Path

ROOT = Path(os.getcwd())
spec = importlib.util.spec_from_file_location("loop_under_test", ROOT / "loop" / "loop.py")
L = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = L   # @dataclass looks its module up here
spec.loader.exec_module(L)

tmp = Path(tempfile.mkdtemp(prefix="loop-gates-"))
for name in ("RUNTIME", "LOGS", "RESULTS", "REPORTS"):
    setattr(L, name, tmp / name.lower())
L.STATE_FILE, L.QUEUE_FILE, L.STOP_FILE = tmp / "state.json", tmp / "human-queue.md", tmp / "STOP"
L.APPROVALS_FILE, L.CONFIG_FILE = tmp / "approvals.txt", tmp / "config.local.json"
L.PID_FILE = tmp / "current.pid"
sent: list = []
L.notify = lambda msg, urgent=False: sent.append(msg)
L.keep_awake = lambda on=True: None
_rel = L.rel
L.rel = lambda p: _rel(p) if Path(p).is_relative_to(L.ROOT) else str(p)   # runtime files live in tmp here

sprint, deps = L.load_sprint(), L.load_deps()
GATED = "1-15-record-a-clip"
assert GATED in (deps.get("gates") or {}), "dependencies.yaml has no gate on 1.15 - nothing to test"


def state_after_1_14(park_rest=False):
    s = {"units": {"1-14-decide-how-the-game-records-video": {"merged": True, "history": [], "human_checks": []}}}
    if park_rest:   # every other startable unit parked, so the gated story is the only work left
        for e in deps["epic_order"]:
            for uid in [f"plan-epic-{e}"] + L.epic_stories(sprint, e):
                if uid != GATED and not L.story_satisfied(sprint, s, uid):
                    s["units"][uid] = {"parked": {"phase": "create", "since": "t", "reason": "test"}}
    return s


def pick(state):
    explain, gated = [], []
    u = L.pick_next(sprint, deps, state, explain, gated)
    return u, gated, explain


bad = 0


def check(label, ok, detail):
    global bad
    bad += not ok
    print("ok  " if ok else "FAIL", label, "->", detail)


L.APPROVALS_FILE.unlink(missing_ok=True)
u, gated, _ = pick(state_after_1_14())
check("1.14 merged, no approval: 1.15 is skipped, other work is picked",
      u is not None and u.id != GATED and (GATED, "ADR-4") in [(k, g["id"]) for k, g in gated], f"next={u and u.id} gated={[(k, g['id']) for k, g in gated]}")

for text, expect_open in [("# APPROVED ADR-4\n", True), ("APPROVED ADR-40\n", True), ("ADR-4 approved\n", True),
                          ("APPROVED ADR-4 2026-11-11 Alexv in chat\n", False), ("  approved adr-4\n", False),
                          ("﻿APPROVED ADR-4\n", False)]:
    L.APPROVALS_FILE.write_text(text, encoding="utf-8")
    u, gated, _ = pick(state_after_1_14(park_rest=True))
    is_open = any(k == GATED for k, _ in gated)
    check(f"approvals.txt {text.strip()!r}: gate {'stays shut' if expect_open else 'opens'}",
          is_open == expect_open and ((u is None) if expect_open else (u is not None and u.id == GATED)),
          f"next={u and u.id} gated={is_open}")

L.APPROVALS_FILE.unlink(missing_ok=True)
L.write_queue(state_after_1_14())
q = L.QUEUE_FILE.read_text(encoding="utf-8")
sec0 = q.split("## 0.")[1].split("## 1.")[0]
check("human queue lists the open gate in section 0", "ADR-4" in sec0 and GATED in sec0, sec0.strip().splitlines()[2][:110])
L.APPROVALS_FILE.write_text("APPROVED ADR-4\n", encoding="utf-8")
L.write_queue(state_after_1_14())
sec0 = L.QUEUE_FILE.read_text(encoding="utf-8").split("## 0.")[1].split("## 1.")[0]
check("human queue drops it once approved", "_None._" in sec0 and "ADR-4" not in sec0, sec0.strip().splitlines()[-1])

# main(): only gated work left -> one gate ping, one idle ping, waits; approval arrives -> starts 1.15
L.APPROVALS_FILE.unlink(missing_ok=True)
L.write_json(L.STATE_FILE, state_after_1_14(park_rest=True))
started: list = []
sleeps = {"n": 0}


def fake_sleep(_s):
    sleeps["n"] += 1
    if sleeps["n"] == 3:   # Alexv approves while the loop is idle
        L.APPROVALS_FILE.write_text("APPROVED ADR-4\n", encoding="utf-8")


def fake_run_unit(u, state):
    started.append(u.id)
    return "stopped"


L.time.sleep, L.run_unit = fake_sleep, fake_run_unit
sys.argv = ["loop.py"]
rc = L.main()
check("main: idles (PC may sleep) and starts 1.15 once approved", started == [GATED] and rc == 0,
      f"started={started} rc={rc} sleeps={sleeps['n']}")
check("main: phone pinged once for the gate and once for going idle",
      len([m for m in sent if "approval of ADR-4" in m]) == 1 and len([m for m in sent if "loop idle" in m]) == 1,
      f"{len(sent)} message(s): {[m[:60] for m in sent]}")
print("FAILURES:", bad)
sys.exit(1 if bad else 0)
