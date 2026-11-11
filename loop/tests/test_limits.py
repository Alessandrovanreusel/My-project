"""Claude usage limits and Alexv's weekly allowance.

Run from the project root:  python loop/tests/test_limits.py
Drives the real run_phase(), run_cross_review() and budget_pause() with a scripted stand-in for the
Claude session and a fake clock (no real waiting). Re-reads real loop logs when they are on this PC.
Written 2026-11-11, when a fix session hit the 5-hour limit and the loop's handling turned out to:
treat any session without a result file as a limit (every log carries routine rate_limit_event
lines), overwrite the cut-off session's log, not tell the next session it had been cut off, save a
cut-off cross-review's "You've hit your limit" as a review with 0 findings, and burn ~43% of the
weekly allowance a day with nothing keeping any of it for Alexv.
"""
import importlib.util
import json
import os
import sys
import tempfile
import time as real_time
from pathlib import Path

ROOT = Path(os.getcwd())
spec = importlib.util.spec_from_file_location("loop_under_test", ROOT / "loop" / "loop.py")
L = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = L
spec.loader.exec_module(L)

tmp = Path(tempfile.mkdtemp(prefix="loop-limits-"))
for name in ("RUNTIME", "LOGS", "RESULTS", "REPORTS"):
    setattr(L, name, tmp / name.lower())
    (tmp / name.lower()).mkdir(parents=True, exist_ok=True)
L.CONFIG_FILE = tmp / "runtime" / "config.local.json"
L.CONFIG_FILE.write_text(json.dumps({"ntfy_topic": "test"}), encoding="utf-8")
L.STOP_FILE = tmp / "runtime" / "STOP"
_rel = L.rel
L.rel = lambda p: _rel(p) if Path(p).is_relative_to(L.ROOT) else str(p)
events: list = []
for name in ("report", "log", "notify"):
    setattr(L, name, lambda msg, *a, _n=name, **k: events.append((_n, msg)))
L.save_state = lambda state: None
L.ensure_unity = lambda: None
L.keep_awake = lambda on=True: events.append(("awake", on))


class FakeTime:
    """The time module as merge/limit code sees it: sleeping only moves the clock (and may run a hook)."""
    def __init__(self, on_sleep=None):
        self.now, self.slept, self.on_sleep = real_time.time(), 0.0, on_sleep

    def time(self):
        return self.now

    def sleep(self, s):
        self.now += s
        self.slept += s
        if self.on_sleep:
            self.on_sleep(self)


def ev(info):
    return json.dumps({"type": "rate_limit_event", "rate_limit_info": info})


def allowed(used7=0.5):
    return ev({"status": "allowed", "unifiedWindows": {"seven_day": {"utilization": used7, "resetsAt": 1.9e9}}})


def rejected(reset):
    return ev({"status": "rejected", "resetsAt": reset, "rateLimitType": "five_hour"})


def result(text, error=False):
    return json.dumps({"type": "result", "is_error": error, "result": text})


bad = 0


def check(label, ok, detail):
    global bad
    bad += not ok
    print("ok  " if ok else "FAIL", label, "->", detail)


# --- 1. what counts as a limit -------------------------------------------------------------------
crash = "\n".join([allowed()] * 14 + ['{"type": "assistant"}'])        # no result at all: died or timed out
check("routine rate_limit_event lines are not a limit", L.usage_limit(crash) == (False, None)
      and L.LIMIT_RE.search(crash), "(the old tail search said: limit)")
check("a rejected snapshot is, with its reset time", L.usage_limit(allowed() + "\n" + rejected(1234.0)) == (True, 1234.0), "")
check("a final error naming a limit is", L.usage_limit(result("You've hit your 5-hour limit", True))[0], "")
check("an API error printed outside the JSON is", L.usage_limit(allowed() + "\nAPI Error: 529 Overloaded")[0], "")
check("a normal finished session is not", L.usage_limit(allowed() + "\n" + result("FINDINGS: 0")) == (False, None), "")

logs = Path(r"c:/Users/alexv/OneDrive/Bureau/Unity/My project/loop/runtime/logs")
real = {"20261111-084912-2-2-walk-the-whole-town-fix-a1.jsonl": True,
        "20261111-033651-2-2-walk-the-whole-town-dev-a1.jsonl": False}
for name, expect in real.items():
    if (logs / name).exists():
        hit, reset = L.usage_limit((logs / name).read_text(encoding="utf-8", errors="replace"))
        check(f"real log {name[16:50]}: limit={expect}", hit == expect and (reset is not None) == expect, (hit, reset))

# --- 2. run_phase: a cut-off session is waited out, resumed with a note, and its log kept ----------
clock = FakeTime()
L.time = clock
sessions = []


def fake_session(script):
    def claude_session(u, phase, prompt, logfile):
        i = len(sessions)
        sessions.append({"prompt": prompt, "log": Path(logfile)})
        kind = script[min(i, len(script) - 1)]
        if kind == "limit":
            text = allowed() + "\n" + rejected(clock.now + 600) + "\n" + result("You've hit your limit", True)
        elif kind == "crash":
            text = "\n".join([allowed()] * 14)
        else:
            text = allowed() + "\n" + result("done")
        Path(logfile).write_text(text + "\n", encoding="utf-8")
        if kind == "ok":   # the session writes the result file its prompt names
            target = Path(prompt.split("Write `")[1].split("`")[0])
            target = target if target.is_absolute() else L.ROOT / target
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(json.dumps({"status": "ok", "summary": "fixed"}), encoding="utf-8")
        return 0, text[-20000:]
    return claude_session


L.claude_session = fake_session(["limit", "ok"])
us = {"attempts": {}, "hint": "", "base_ref": "abc123", "findings": None}
res = L.run_phase(L.Unit("story", "x", 1), us, "fix", {})
check("cut off by a limit: waited until the named reset, then a fresh session finished",
      res.get("status") == "ok" and len(sessions) == 2 and 600 <= clock.slept <= 800,
      f"status={res.get('status')} sessions={len(sessions)} waited={clock.slept:.0f}s")
check("...the cut-off session's log is kept (a new file for the retry)",
      sessions[0]["log"] != sessions[1]["log"] and sessions[0]["log"].exists(), [s["log"].name for s in sessions])
check("...and the next session is told it was cut off and to check git first",
      "cut off by a Claude usage limit" in sessions[1]["prompt"] and "git log abc123..HEAD" in sessions[1]["prompt"],
      "note present" if "cut off" in sessions[1]["prompt"] else "no note")
check("...a 10-minute wait does not ping the phone", not [m for n, m in events if n == "notify"], "")

sessions.clear()
clock.slept = 0
L.claude_session = fake_session(["crash"])
res = L.run_phase(L.Unit("story", "x", 1), {"attempts": {}, "hint": ""}, "dev", {})
check("a crash without a result is a failed attempt at once, not a 30-hour limit wait",
      res.get("status") == "failed" and len(sessions) == 1 and clock.slept == 0,
      f"status={res.get('status')} sessions={len(sessions)} waited={clock.slept:.0f}s")

# --- 3. cross-review: a cut-off reviewer's limit message is never saved as the review -------------
calls = []


def fake_process(cmd, prompt, logfile, timeout, env):
    i = len(calls)
    calls.append(logfile)
    text = (rejected(clock.now + 300) + "\n" + result("You've hit your 5-hour limit", True)) if i == 0 \
        else (allowed() + "\n" + result("FINDINGS: 0\n\nRead everything; nothing wrong."))
    Path(logfile).write_text(text + "\n", encoding="utf-8")
    return 0, text


L.run_process = fake_process
L.review_coverage = lambda base, branch, text: ([], [])
L.dirty_tracked = lambda: []
path = L.run_cross_review(L.Unit("story", "x", 1), {"base_ref": "abc123"})
saved = Path(path).read_text(encoding="utf-8") if path else ""
check("cross-review cut off by a limit: waited and asked again; the limit text is not the review",
      len(calls) == 2 and "hit your" not in saved and saved.startswith("FINDINGS: 0"), saved[:60] or "nothing saved")

# --- 4. the weekly budget ------------------------------------------------------------------------
def snapshot(used, reset_in):
    f = L.LOGS / f"zz-{len(list(L.LOGS.glob('zz-*')))}.jsonl"
    f.write_text(allowed() + "\n" + ev({"status": "allowed", "unifiedWindows": {
        "seven_day": {"utilization": used, "resetsAt": real_time.time() + reset_in}}}) + "\n", encoding="utf-8")
    os.utime(f, (real_time.time() + 100 + len(list(L.LOGS.glob('zz-*'))),) * 2)   # the newest log


def budget(cfg=None, on_sleep=None):
    global clock
    clock = FakeTime(on_sleep)
    L.time = clock
    L.CONFIG_FILE.write_text(json.dumps({"ntfy_topic": "test", **(cfg or {})}), encoding="utf-8")
    events.clear()
    state = {}
    stopped = L.budget_pause(state)
    return stopped, clock.slept, [m for n, m in events if n == "notify"], state


snapshot(0.50, 3 * 86400)
stopped, slept, pings, _ = budget()
check("under budget: no pause", not stopped and slept == 0 and not pings, f"slept {slept:.0f}s")

snapshot(0.93, 2 * 86400)
stopped, slept, pings, state = budget()
check("over the 90% default: sleeps until the weekly reset, pings once, then goes on",
      not stopped and 2 * 86400 <= slept <= 2 * 86400 + 200 and len(pings) == 1 and "budget_pause" not in state,
      f"slept {slept / 3600:.1f} h; pings {pings[:1]}")
check("...and lets the PC sleep meanwhile, awake again after", ("awake", False) in events and events[-1] != ("awake", False),
      [e for e in events if e[0] == "awake"])

stopped, slept, pings, _ = budget({"weekly_budget": 1.0})
check("weekly_budget 1.0 in config.local.json: no pause", not stopped and slept == 0, f"slept {slept:.0f}s")

stopped, slept, pings, _ = budget(on_sleep=lambda c: c.slept >= 3600 and L.CONFIG_FILE.write_text(
    json.dumps({"ntfy_topic": "test", "weekly_budget": 0.99}), encoding="utf-8"))
check("raising the budget while it waits lets it go on within a minute",
      not stopped and 3600 <= slept <= 3700, f"slept {slept / 60:.0f} min")

stopped, slept, pings, _ = budget(on_sleep=lambda c: c.slept >= 600 and L.STOP_FILE.write_text("x"))
L.STOP_FILE.unlink(missing_ok=True)
check("Alexv's stop is obeyed during the pause", stopped and slept < 700, f"slept {slept / 60:.0f} min")

snapshot(0.95, -60)
stopped, slept, pings, _ = budget()
check("a full window whose reset time has passed: no pause", not stopped and slept == 0, f"slept {slept:.0f}s")

L.time = real_time
print("FAILURES:", bad)
sys.exit(1 if bad else 0)
