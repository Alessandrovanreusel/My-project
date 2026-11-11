"""Merging a finished unit: a real conflict parks at once; a merge that fails for any other reason
(a file held open by Unity, OneDrive or a virus scan) is retried, and never strands files on master.

Run from the project root:  python loop/tests/test_merge_retry.py
Drives the real merge_unit() against throw-away git repos in a temp folder. The lock is real: this
process holds a file open (Python opens files without delete-sharing on Windows, so git cannot replace
it), exactly as another program would. Written after 2026-11-11, when Story 2.1 was parked for a
"merge conflict" that git merge-tree showed did not exist, with its new files left behind on master.
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


class Clock:
    """Stands in for the time module inside merge_unit: a 'wait' runs whatever the scenario says the
    world does meanwhile (e.g. the other program lets go of the file)."""
    def __init__(self, on_sleep=None):
        self.sleeps, self.on_sleep = 0, on_sleep

    def sleep(self, s):
        self.sleeps += 1
        if self.on_sleep:
            self.on_sleep()

    def __getattr__(self, name):
        import time
        return getattr(time, name)


def repo(conflict=False):
    d = Path(tempfile.mkdtemp(prefix="loop-merge-"))
    run = lambda *a: subprocess.run(["git", *a], cwd=d, capture_output=True, text=True, check=True,
                                    stdin=subprocess.DEVNULL)
    run("init", "-q", "-b", "master")
    run("config", "user.email", "loop@test"), run("config", "user.name", "loop test")
    run("config", "core.autocrlf", "false")
    (d / "z.txt").write_text("master v1\n")
    run("add", "."), run("commit", "-q", "-m", "base")
    run("checkout", "-q", "-b", "loop/x")
    (d / "new").mkdir()
    (d / "new" / "b.txt").write_text("added by the story\n")
    (d / "z.txt").write_text("story v2\n")
    run("add", "."), run("commit", "-q", "-m", "story")
    run("checkout", "-q", "master")
    if conflict:
        (d / "z.txt").write_text("master v3\n")
        run("add", "."), run("commit", "-q", "-m", "master moves on")
    L.ROOT = d
    return d, run


def merge(on_sleep=None):
    events.clear()
    L.time = Clock(on_sleep)
    us = {"history": [{"summary": "done"}], "lessons": [], "human_checks": []}
    ok = L.merge_unit(L.Unit("story", "x", 1), us, {})
    return ok, us, L.time.sleeps


def status(run):
    return run("status", "--porcelain", "--untracked-files=all").stdout.strip()


bad = 0


def check(label, ok, detail):
    global bad
    bad += not ok
    print("ok  " if ok else "FAIL", label, "->", detail)


# 1. Clean merge, nothing in the way.
d, run = repo()
ok, us, sleeps = merge()
check("clean: merged first time, no waiting", ok and us.get("merged") and sleeps == 0,
      f"ok={ok} sleeps={sleeps} head={run('log', '--oneline', '-1').stdout.strip()}")

# 2. A real conflict: parked at once, named, and the merge is never even attempted.
d, run = repo(conflict=True)
ok, us, sleeps = merge()
reason = (us.get("parked") or {}).get("reason", "")
check("conflict: parked immediately, the file named", not ok and "conflict" in reason and "z.txt" in reason
      and sleeps == 0, reason[:100])
check("conflict: master untouched and clean", status(run) == "" and "master moves on" in run("log", "-1").stdout,
      status(run) or "clean")

# 3. A file held open during the first attempt, then let go: retried and merged, nothing left behind.
d, run = repo()
held = open(d / "z.txt", "rb")
ok, us, sleeps = merge(on_sleep=lambda: held.closed or held.close())
held.close()
fails = [m for n, m in events if n == "log" and "failed, not a conflict" in m]
check("locked once: the failure is real (the lock works on this machine)", len(fails) >= 1,
      fails[0][:150] if fails else "git replaced a file this process held open: the lock simulation is broken")
check("locked once: retried and merged", ok and us.get("merged") and sleeps >= 1, f"ok={ok} sleeps={sleeps}")
check("locked once: working tree clean, story's file in place", status(run) == "" and (d / "new" / "b.txt").exists(),
      status(run) or "clean")

# 4. A file held open the whole time: parked as a machine problem, not a conflict, and master stays clean.
d, run = repo()
held = open(d / "z.txt", "rb")
ok, us, sleeps = merge()
left = status(run)
held.close()
reason = (us.get("parked") or {}).get("reason", "")
check("locked throughout: parked after every try, says it is not a conflict",
      not ok and sleeps == L.MERGE_TRIES - 1 and "no conflicts" in reason, reason[:120])
check("locked throughout: no half-merged files stranded on master", left == "" and not (d / "new" / "b.txt").exists(),
      left or "clean")
check("locked throughout: master did not move", run("log", "--oneline", "-1").stdout.split()[1] == "base",
      run("log", "--oneline", "-1").stdout.strip())

# 5. remove_half_merged only removes copies identical to the branch's own files.
d, run = repo()
(d / "new").mkdir()
(d / "new" / "b.txt").write_text("added by the story\n")          # identical: a half-merge leftover
(d / "notes.txt").write_text("somebody's own file\n")             # the branch has no such file
removed = L.remove_half_merged("loop/x")
check("cleanup: removes the identical leftover", removed == ["new/b.txt"] and not (d / "new").exists(), removed)
(d / "new").mkdir()
(d / "new" / "b.txt").write_text("edited by hand\n")              # same path, different content
removed = L.remove_half_merged("loop/x")
check("cleanup: keeps a different file at the same path, and files the branch lacks",
      removed == [] and (d / "new" / "b.txt").exists() and (d / "notes.txt").exists(), removed)

L.ROOT = ROOT
print("FAILURES:", bad)
sys.exit(1 if bad else 0)
