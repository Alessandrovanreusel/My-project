"""How phase results accumulate on a unit: checks are replaced by the newest full list, never duplicated.

Run from the project root:  python loop/tests/test_absorb_result.py
Replays Story 1.14's real dev, review and close result files when they are still on this PC (they
gave Alexv 12 checks for 4 before this fix), then edge cases. Exits 1 on any disagreement.
"""
import glob
import importlib.util
import json
import os
import sys
from pathlib import Path

ROOT = Path(os.getcwd())
spec = importlib.util.spec_from_file_location("loop_under_test", ROOT / "loop" / "loop.py")
L = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = L
spec.loader.exec_module(L)

bad = 0


def check(label, ok, detail):
    global bad
    bad += not ok
    print("ok  " if ok else "FAIL", label, "->", detail)


def fresh():
    return {"decisions": [], "human_checks": [], "lessons": []}


real = sorted(glob.glob(str(Path(r"c:/Users/alexv/OneDrive/Bureau/Unity/My project/loop/runtime/results/"
                                 r"1-14-decide-how-the-game-records-video") / "*-a*-*.json")))
real = [p for p in real if Path(p).name.split("-")[0] in ("create", "dev", "review", "close")]
real.sort(key=lambda p: Path(p).stem.split("-", 2)[2])   # <phase>-a<n>-<stamp>: replay in the order they ran
if real:
    us = fresh()
    phases = []
    for p in real:
        res = json.loads(Path(p).read_text(encoding="utf-8"))
        L.absorb_result(us, res)
        phases.append((Path(p).name.split("-")[0], len(res.get("human_checks") or [])))
    last = json.loads(Path(real[-1]).read_text(encoding="utf-8"))
    check("Story 1.14 replay: Alexv sees the close phase's checks once, not every phase's",
          us["human_checks"] == last["human_checks"], f"phases {phases} -> {len(us['human_checks'])} check(s)")
    check("Story 1.14 replay: lessons have no exact repeats", len(us["lessons"]) == len(set(us["lessons"])),
          f"{len(us['lessons'])} lessons")

us = fresh()
L.absorb_result(us, {"human_checks": [{"ac": "AC1"}], "decisions": [{"question": "q1"}], "lessons": ["x"]})
L.absorb_result(us, {"human_checks": [], "decisions": [{"question": "q2"}], "lessons": ["x", "y"]})
check("an empty list never clears open checks", us["human_checks"] == [{"ac": "AC1"}], us["human_checks"])
check("decisions accumulate", [d["question"] for d in us["decisions"]] == ["q1", "q2"], us["decisions"])
check("a repeated lesson is kept once", us["lessons"] == ["x", "y"], us["lessons"])
L.absorb_result(us, {"human_checks": [{"ac": "AC1"}, {"ac": "AC2"}]})
check("a later full list replaces the earlier one", [c["ac"] for c in us["human_checks"]] == ["AC1", "AC2"], us["human_checks"])
L.absorb_result(us, {})
check("a result with no lists changes nothing", len(us["human_checks"]) == 2 and len(us["decisions"]) == 2, "unchanged")
print("FAILURES:", bad)
sys.exit(1 if bad else 0)
