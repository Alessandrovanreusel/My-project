#!/usr/bin/env python3
"""Unattended BMad loop for Camera Game — the orchestrator. Read loop/README.md first.

It picks the next unit of work (a story, the planning of an epic, or an epic's retrospective) from
sprint-status.yaml + loop/dependencies.yaml, runs each phase of that unit in a FRESH headless Claude
session on its own git branch, cross-reviews stories with a read-only Claude session on a different
model, merges to master when the unit
passes, and parks anything genuinely blocked so it can move on to work that does not depend on it.

Only the files are state: kill it at any point and start it again; it resumes where it stopped.

  python loop/loop.py              run until everything is done or blocked
  python loop/loop.py --only KEY   run one unit (the pilot), then stop
  python loop/loop.py --dry-run    show what it would pick next, and why, without running anything
  python loop/loop.py --smoke      one tiny session that checks Unity MCP, Chrome, Blender and the guard
"""
from __future__ import annotations

import argparse
import ctypes
import datetime as dt
import hashlib
import itertools
import json
import os
import re
import secrets
import shutil
import socket
import ssl
import subprocess
import sys
import time
import traceback
import urllib.request
from dataclasses import dataclass
from pathlib import Path

import yaml

# The console may not be UTF-8 (a pipe on Windows is cp1252), and the log uses "→" and "—". A crash in
# print() must never take the loop down, so encode as UTF-8 and replace anything unprintable.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

ROOT = Path(__file__).resolve().parent.parent
LOOP = ROOT / "loop"
PROMPTS = LOOP / "prompts"
RUNTIME = LOOP / "runtime"
LOGS = RUNTIME / "logs"
RESULTS = RUNTIME / "results"
REPORTS = RUNTIME / "reports"
STATE_FILE = RUNTIME / "state.json"
QUEUE_FILE = RUNTIME / "human-queue.md"
CONFIG_FILE = RUNTIME / "config.local.json"
STOP_FILE = RUNTIME / "STOP"
APPROVALS_FILE = RUNTIME / "approvals.txt"   # Alexv's sign-offs; safe to edit while the loop runs
GATE_POLL_S = 600                            # idle re-check of approvals.txt when only gated work is left
MERGE_TRIES = 3                              # a merge with no conflicts that still fails is retried...
MERGE_RETRY_S = 20                           # ...after this long (a file held by Unity/OneDrive/a scan)
PID_FILE = RUNTIME / "current.pid"
# The loop runs from the working tree, which is usually a unit's branch, and that branch only picks up
# master's loop fixes when the loop brings master in, AFTER this file was loaded. On 2026-11-11 a restart
# meant to load a fix ran the old code for 14 hours. So between phases, if this file changed on disk,
# the loop saves its state and restarts itself on the new code (RELOAD).
CODE_FILE = Path(__file__).resolve()
RELOAD = 3
# A session's time limit counts only time the PC was awake. A 10 s poll that took longer than this
# means Windows slept (lid closed, battery): on 2026-11-11 a session frozen by standby at 13:32 was
# killed for "running 2 h" the moment the PC woke at 23:31, after 82 min of real work.
SLEEP_GAP_S = 120
DEPS_FILE = LOOP / "dependencies.yaml"
LESSONS_FILE = LOOP / "lessons.md"
IMPL = ROOT / "_bmad-output" / "implementation-artifacts"
SPRINT_FILE = IMPL / "sprint-status.yaml"

UNITY_EXE = r"C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe"
UNITY_HUB = r"C:\Program Files\Unity Hub\Unity Hub.exe"
EDITOR_LOG = Path(os.environ.get("LOCALAPPDATA", "")) / "Unity" / "Editor" / "Editor.log"
LICENCE_RE = re.compile(r"Access token is unavailable|Found 0 entitlement groups|No valid Unity Editor license", re.I)
CLAUDE = shutil.which("claude") or "claude"
REVIEW_MODEL = "claude-sonnet-5-5"   # cross-review runs on a different model from the Opus build sessions
ATTRIBUTION = "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"

STORY_PHASES = ["create", "dev", "review", "cross-review", "fix", "close"]
HOURS = {"create": 2, "dev": 8, "review": 5, "cross-review": 1, "fix": 4, "close": 2,
         "plan-epic": 3, "retro": 2, "smoke": 0.5}
NEEDS_UNITY = {"create", "dev", "review", "fix", "close", "smoke"}
MAX_ATTEMPTS = 3                # failed attempts that committed NOTHING before a phase is parked...
MAX_RUNS = 6                    # ...and failed attempts of any kind (handovers included)
MAX_UNITY_RESTARTS = 2          # consecutive, before the whole loop stops
LIMIT_WAIT_S = 20 * 60          # usage limit that names no reset time: wait this long, then retry...
LIMIT_WAIT_MAX_S = 30 * 3600    # ...for up to this long in all
LIMIT_RESET_MAX_S = 8 * 86400   # a limit that names its reset time is waited out (the weekly one too)
LIMIT_RE = re.compile(r"usage limit|limit reached|hit your (?:[\w-]+ )?limit|rate.?limit|overloaded|"
                      r"too many requests|\b429\b|\b529\b|credit balance", re.I)
# Alexv's Claude allowance is shared with the loop. Measured 2026-11-11: the loop used ~43% of the
# 7-day allowance per day, so running flat out it would lock him out of Claude for days before the
# weekly reset. Between phases, the loop pauses once the 7-day window is this full and resumes when
# it resets. Alexv can change it with "weekly_budget" in config.local.json (1.0 = use everything).
WEEKLY_BUDGET = 0.90
DISALLOWED = "AskUserQuestion,Workflow,EnterPlanMode,ExitPlanMode"
REVIEW_DISALLOWED = DISALLOWED + ",Edit,Write,NotebookEdit,mcp__UnityMCP,mcp__blender,mcp__claude-in-chrome"
# A cross-review is only trusted if the reviewer demonstrably SAW the code. Measured 2026-11-10: at
# default effort, with no coverage check, it answered "FINDINGS: 0" on Story 1.14 after 16 s and four
# commands, having opened 3 of its 10 changed source files.
REVIEW_EFFORT = "high"
REVIEW_MIN_COVERAGE = 0.8       # share of changed source files whose text must reach the reviewer
REVIEW_FILE_SEEN = 0.6          # share of a file's distinctive lines that must appear in its tool output...
REVIEW_CHANGE_SEEN = 0.9        # ...or share of its distinctive CHANGED lines (a diff review of that file counts)
REVIEW_SOURCE_RE = re.compile(r"^(Assets|Packages)/.+\.(cs|asmdef|asmref|shader|hlsl|cginc|compute|uss|uxml)$", re.I)
# Verification rigs are tools, not review subjects. Editor-folder rigs by name; their runtime halves
# (runners, feeds) by name AND only when the whole file sits behind #if UNITY_EDITOR, so a shipped
# class that happens to be called "...Runner" is still reviewed. (Case-sensitive "Rig[A-Z]": not "Rigidbody".)
REVIEW_RIG_RE = re.compile(r"^Assets/Scripts/Editor/[^/]*Rig[^/]*$", re.I)
REVIEW_RIG_RUNTIME_RE = re.compile(r"/(?:\w*Runner|Rig[A-Z]\w*)(?:\.\w+)?\.cs$")   # incl. partials: TownRigRunner.Edges.cs


@dataclass
class Unit:
    kind: str   # story | plan-epic | retro
    id: str     # sprint key, or plan-epic-N / retro-epic-N
    epic: int

    @property
    def branch(self) -> str:
        return f"loop/{self.id}"

    @property
    def phases(self) -> list[str]:
        return STORY_PHASES if self.kind == "story" else [self.kind]


# ----------------------------------------------------------------------------- small utilities

def now() -> str:
    return dt.datetime.now().strftime("%Y-%m-%d %H:%M:%S")


def log(msg: str) -> None:
    line = f"[{now()}] {msg}"
    print(line, flush=True)
    LOGS.mkdir(parents=True, exist_ok=True)
    with open(LOGS / "orchestrator.log", "a", encoding="utf-8") as f:
        f.write(line + "\n")


def report(msg: str) -> None:
    REPORTS.mkdir(parents=True, exist_ok=True)
    day = dt.date.today().isoformat()
    path = REPORTS / f"{day}.md"
    first = not path.exists()
    with open(path, "a", encoding="utf-8") as f:
        if first:
            f.write(f"# Loop report — {day}\n\n")
        f.write(f"- {dt.datetime.now().strftime('%H:%M')} {msg}\n")


def write_json(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")
    os.replace(tmp, path)


def rel(p: Path) -> str:
    return p.relative_to(ROOT).as_posix()


def code_digest(path: Path) -> str:
    try:
        return hashlib.sha1(path.read_bytes()).hexdigest()
    except OSError:
        return ""


CODE_DIGEST = code_digest(CODE_FILE)


def code_changed() -> bool:
    """Has loop.py on disk changed since this process loaded it (a merge or checkout brought new code)?"""
    now_digest = code_digest(CODE_FILE)
    return bool(now_digest) and now_digest != CODE_DIGEST


def keep_awake(on: bool = True) -> None:
    """Stop Windows sleeping while the loop works (on=False lets it sleep again while the loop only
    waits for Alexv), without changing any power setting."""
    try:
        ctypes.windll.kernel32.SetThreadExecutionState(0x80000000 | (0x00000001 if on else 0))
    except Exception:
        pass


def config() -> dict:
    cfg = json.loads(CONFIG_FILE.read_text(encoding="utf-8")) if CONFIG_FILE.exists() else {}
    if not cfg.get("ntfy_topic"):
        cfg["ntfy_topic"] = "cgl-" + secrets.token_hex(12)   # private: never commit this file
        write_json(CONFIG_FILE, cfg)
    return cfg


def notify(msg: str, urgent: bool = False) -> None:
    """One line to Alexv's phone via ntfy. Only for blockers and full stops, never routine progress."""
    topic = config().get("ntfy_topic")
    log(f"NOTIFY{' (urgent)' if urgent else ''}: {msg}")
    url = f"https://ntfy.sh/{topic}"
    headers = {"Title": "Game loop", "Priority": "high" if urgent else "default",
               "Tags": "rotating_light" if urgent else "pause_button"}
    try:
        # Python's default certificate store on this machine is stale ("certificate has expired" for a
        # valid Let's Encrypt chain, measured 2026-11-10). Verify against certifi's current bundle instead
        # of turning verification off.
        try:
            import certifi
            ctx = ssl.create_default_context(cafile=certifi.where())
        except Exception:
            ctx = ssl.create_default_context()
        req = urllib.request.Request(url, data=msg[:200].encode("utf-8"), method="POST", headers=headers)
        urllib.request.urlopen(req, timeout=15, context=ctx).read()
    except Exception as e:  # fall back to Windows' own curl (schannel store); never let this stop the loop
        r = subprocess.run([r"C:\Windows\System32\curl.exe", "-sS", "-o", "NUL", "-w", "%{http_code}",
                            *sum((["-H", f"{k}: {v}"] for k, v in headers.items()), []),
                            "--data-binary", msg[:200], url], capture_output=True, text=True)
        if r.stdout.strip() != "200":
            log(f"ntfy failed: {e}; curl fallback: {r.stdout.strip()} {r.stderr.strip()[:200]}")


def stop_requested() -> bool:
    return STOP_FILE.exists()


# ----------------------------------------------------------------------------- git

def git(*args: str, check: bool = True) -> subprocess.CompletedProcess:
    # No stdin: with a console attached, Git for Windows answers a locked file with "Unlink of file ...
    # failed. Should I try again? (y/n)" and waits for a reply nobody will type.
    r = subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
                       errors="replace", stdin=subprocess.DEVNULL)
    if check and r.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {r.stderr.strip() or r.stdout.strip()}")
    return r


def current_branch() -> str:
    return git("rev-parse", "--abbrev-ref", "HEAD").stdout.strip()


def branch_exists(name: str) -> bool:
    return git("rev-parse", "--verify", "--quiet", f"refs/heads/{name}", check=False).returncode == 0


def commit_tracked_leftovers(message: str) -> bool:
    """Commit tracked files a session (or the editor) changed but did not commit. Never untracked ones."""
    if not git("status", "--porcelain", "--untracked-files=no").stdout.strip():
        return False
    git("add", "-u")
    git("commit", "-m", f"{message}\n\n{ATTRIBUTION}")
    return True


def dirty_tracked() -> list[str]:
    """Tracked files with uncommitted changes (renames report their new path)."""
    out = git("status", "--porcelain", "--untracked-files=no").stdout
    return [l[3:].split(" -> ")[-1].strip().strip('"') for l in out.splitlines() if l.strip()]


def untracked_project_files() -> list[str]:
    out = git("status", "--porcelain", "--", "Assets", "_bmad-output/planning-artifacts",
              "_bmad-output/implementation-artifacts", "loop/dependencies.yaml").stdout
    return [l[3:] for l in out.splitlines() if l.startswith("??")]


def push(*refs: str) -> None:
    r = git("push", "origin", *refs, check=False)
    if r.returncode != 0:
        log(f"push {' '.join(refs)} failed (non-fatal): {r.stderr.strip()[:300]}")


def sync_master() -> None:
    git("fetch", "origin", "master", check=False)
    git("merge", "--ff-only", "origin/master", check=False)


# ----------------------------------------------------------------------------- unity

class InfraStop(Exception):
    """The machine, not the story, is broken (Unity cannot start). Stop everything and tell Alexv."""


def process_running(image: str) -> bool:
    r = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {image}", "/NH"], capture_output=True, text=True)
    return image.lower() in r.stdout.lower()


def unity_running() -> bool:
    return process_running("Unity.exe")


def bridge_ports() -> set[int]:
    ports = {6400}
    for f in (Path.home() / ".unity-mcp").glob("unity-mcp-port*.json"):
        try:
            ports.add(int(json.loads(f.read_text(encoding="utf-8")).get("unity_port")))
        except Exception:
            pass
    return ports


def bridge_up() -> bool:
    """True once the MCP for Unity bridge inside the editor accepts connections, i.e. tools will work."""
    for port in bridge_ports():
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=2):
                return True
        except OSError:
            continue
    return False


def ensure_unity() -> None:
    """Make sure an editor with this project is open AND its MCP bridge answers. Raise InfraStop if not."""
    if unity_running():
        for _ in range(60):          # it may be importing or mid domain-reload
            if bridge_up():
                return
            time.sleep(10)
        raise InfraStop("UNITY: the editor is running but its MCP bridge never answered (a modal dialog?)")
    if not process_running("Unity Hub.exe"):
        log("starting Unity Hub (the editor gets its licence through it)")
        subprocess.Popen([UNITY_HUB], creationflags=getattr(subprocess, "DETACHED_PROCESS", 0))
        time.sleep(45)
    log("Unity is not running: starting the editor")
    launched = time.time()
    subprocess.Popen([UNITY_EXE, "-projectPath", str(ROOT)], cwd=ROOT,
                     creationflags=getattr(subprocess, "DETACHED_PROCESS", 0))
    time.sleep(20)
    for _ in range(90):              # up to 15 minutes for import + compile + bridge
        fresh_log = EDITOR_LOG.exists() and EDITOR_LOG.stat().st_mtime >= launched - 5
        text = EDITOR_LOG.read_text(encoding="utf-8", errors="replace") if fresh_log else ""
        if LICENCE_RE.search(text):
            raise InfraStop("Unity cannot start: licence unavailable. Open Unity Hub and sign in, then start the loop again")
        if not unity_running():
            raise InfraStop(f"Unity exited during startup; see {EDITOR_LOG}")
        if bridge_up():
            time.sleep(20)           # let the first compile settle
            log("Unity is up and its MCP bridge answers")
            return
        time.sleep(10)
    raise InfraStop("UNITY: the editor started but its MCP bridge did not answer within 15 minutes")


def restart_unity() -> None:
    log("Restarting Unity")
    subprocess.run(["taskkill", "/IM", "Unity.exe", "/F"], capture_output=True)
    time.sleep(15)
    ensure_unity()


# ----------------------------------------------------------------------------- state

def load_state() -> dict:
    if STATE_FILE.exists():
        return json.loads(STATE_FILE.read_text(encoding="utf-8"))
    return {"units": {}, "unity_restarts": 0, "started": now()}


def save_state(state: dict) -> None:
    state["heartbeat"] = now()
    write_json(STATE_FILE, state)
    write_queue(state)


def unit_state(state: dict, u: Unit) -> dict:
    return state["units"].setdefault(u.id, {
        "kind": u.kind, "epic": u.epic, "phase": u.phases[0], "attempts": {}, "parked": None,
        "merged": False, "base_ref": None, "decisions": [], "human_checks": [], "lessons": [],
        "history": [], "findings": None, "hint": "",
    })


def load_sprint() -> dict:
    return yaml.safe_load(SPRINT_FILE.read_text(encoding="utf-8"))["development_status"]


def load_deps() -> dict:
    return yaml.safe_load(DEPS_FILE.read_text(encoding="utf-8"))


def epic_stories(sprint: dict, epic: int) -> list[str]:
    return [k for k in sprint if re.match(rf"^{epic}-\d+-", k)]


def story_satisfied(sprint: dict, state: dict, key: str) -> bool:
    return sprint.get(key) == "done" or state["units"].get(key, {}).get("merged", False)


def epic_satisfied(sprint: dict, state: dict, epic: int) -> bool:
    stories = epic_stories(sprint, epic)
    return bool(stories) and all(story_satisfied(sprint, state, k) for k in stories)


def parked(state: dict, unit_id: str) -> bool:
    return bool(state["units"].get(unit_id, {}).get("parked"))


def approvals() -> set[str]:
    """Gate ids Alexv has approved: each line `APPROVED <id> ...` in loop/runtime/approvals.txt.
    Re-read on every pick, so an approval takes effect without restarting the loop."""
    try:
        text = APPROVALS_FILE.read_text(encoding="utf-8-sig")
    except OSError:
        return set()
    return {m.group(1).upper() for m in re.finditer(r"^[ \t]*APPROVED[ \t]+(\S+)", text, re.M | re.I)}


def open_gates(deps: dict, uid: str, approved: set[str]) -> list[dict]:
    """The approvals (dependencies.yaml `gates`) that unit still waits for. A gated unit is never
    started; the loop does work that does not depend on it instead."""
    return [g for g in (deps.get("gates") or {}).get(uid, []) if str(g["id"]).upper() not in approved]


def pick_next(sprint: dict, deps: dict, state: dict, explain: list | None = None,
              gated: list | None = None) -> Unit | None:
    """`gated` collects (unit, gate) pairs for units that are ready except for Alexv's approval."""
    def why(msg):
        if explain is not None:
            explain.append(msg)

    approved = approvals()

    def held(uid: str) -> bool:
        open_ = open_gates(deps, uid, approved)
        if open_:
            why(f"{uid}: waits for Alexv's approval of {', '.join(str(g['id']) for g in open_)}")
            if gated is not None:
                gated.extend((uid, g) for g in open_)
        return bool(open_)

    order = deps["epic_order"]
    epic_deps = {int(k): v for k, v in (deps.get("epic_depends_on") or {}).items()}
    story_deps = deps.get("story_depends_on") or {}

    for e in order:   # 1. a finished epic gets its retrospective first
        if epic_satisfied(sprint, state, e) and sprint.get(f"epic-{e}-retrospective") != "done":
            uid = f"retro-epic-{e}"
            if not parked(state, uid) and not state["units"].get(uid, {}).get("merged") and not held(uid):
                return Unit("retro", uid, e)

    for e in order:   # 2. stories in preferred order, skipping past anything blocked
        blockers = [d for d in epic_deps.get(e, []) if not epic_satisfied(sprint, state, d)]
        stories = epic_stories(sprint, e)
        if blockers:
            why(f"epic {e}: waits for epic(s) {blockers}")
            continue
        if not stories:
            uid = f"plan-epic-{e}"
            if parked(state, uid):
                why(f"epic {e}: planning is parked")
                continue
            if state["units"].get(uid, {}).get("merged"):
                why(f"epic {e}: planned and merged, but sprint-status has no stories for it (check the plan)")
                continue
            if held(uid):
                continue
            return Unit("plan-epic", uid, e)
        for k in stories:
            if story_satisfied(sprint, state, k):
                continue
            if parked(state, k):
                why(f"{k}: parked ({state['units'][k]['parked']['reason'][:80]})")
                continue
            waiting = [d for d in story_deps.get(k, []) if not story_satisfied(sprint, state, d)]
            if waiting:
                why(f"{k}: waits for {waiting}")
                continue
            if held(k):
                continue
            return Unit("story", k, e)
        why(f"epic {e}: nothing eligible")
    return None


def unit_from_id(uid: str) -> Unit:
    m = re.match(r"^(plan-epic|retro-epic)-(\d+)$", uid)
    if m:
        return Unit("plan-epic" if m.group(1) == "plan-epic" else "retro", uid, int(m.group(2)))
    return Unit("story", uid, int(uid.split("-")[0]))


# ----------------------------------------------------------------------------- the human queue

def write_queue(state: dict) -> None:
    lines = ["# Waiting for Alexv", "",
             "Regenerated by `loop/loop.py` after every phase. Four kinds of item, in order of urgency.", ""]
    try:
        sprint, deps, approved = load_sprint(), load_deps(), approvals()
        held = [(uid, g) for uid, gates in (deps.get("gates") or {}).items()
                if not story_satisfied(sprint, state, uid) for g in gates
                if str(g["id"]).upper() not in approved]
    except Exception as e:   # the queue must still be written if a planning file is mid-edit
        log(f"human queue: could not read the gates ({e})")
        held = []
    lines += ["## 0. Approvals — this work waits for you (the loop does other work meanwhile)", ""]
    lines += [f"- [ ] **{g['id']}** holds back **{uid}**: {g.get('why', '')}  \n"
              f"  Read: {g.get('read', '-')}  \n"
              f"  To approve: add the line `APPROVED {g['id']}` to `loop/runtime/approvals.txt`, or tell "
              f"Claude \"approve {g['id']}\". To veto: tell Claude what you want instead." for uid, g in held] or ["_None._"]
    lines += [""]
    blocked = [(k, v) for k, v in state["units"].items() if v.get("parked")]
    lines += ["## 1. Blockers — the loop could not continue these", ""]
    lines += [f"- **{k}** (phase `{v['parked']['phase']}`, since {v['parked']['since']}): "
              f"{v['parked']['reason']}  \n  To resume: fix it, then delete its `parked` entry in "
              f"`loop/runtime/state.json` (or tell Claude \"resume {k}\")." for k, v in blocked] or ["_None._"]
    lines += ["", "## 2. Decisions the loop took for you — veto any you disagree with", ""]
    n = 0
    for k, v in state["units"].items():
        for d in v.get("decisions", []):
            n += 1
            lines.append(f"- [ ] **{k}** — {d.get('question', '?')}  \n  Chose: **{d.get('chosen', '?')}** "
                         f"(options: {', '.join(d.get('options') or []) or '-'}). Evidence: `{d.get('evidence', '-')}`. "
                         f"Cost to reverse: {d.get('reversal_cost', '?')}")
    if not n:
        lines.append("_None._")
    lines += ["", "## 3. Checks that need your eyes or ears — do them in one sitting", ""]
    n = 0
    for k, v in state["units"].items():
        for c in v.get("human_checks", []):
            n += 1
            ev = ", ".join(f"`{p}`" for p in (c.get("evidence") or [])) or "-"
            lines.append(f"- [ ] **{k}** — {c.get('ac', '?')}: {c.get('look_at', '?')}  \n  Evidence: {ev}  \n"
                         f"  Loop's own read: {c.get('my_read', '-')}")
    if not n:
        lines.append("_None._")
    QUEUE_FILE.parent.mkdir(parents=True, exist_ok=True)
    QUEUE_FILE.write_text("\n".join(lines) + "\n", encoding="utf-8")


# ----------------------------------------------------------------------------- running sessions

def render(phase: str, ctx: dict, with_rules: bool = True) -> str:
    body = (PROMPTS / f"{phase}.md").read_text(encoding="utf-8")
    text = ((PROMPTS / "_rules.md").read_text(encoding="utf-8") + "\n\n---\n\n" + body) if with_rules else body
    for k, v in ctx.items():
        text = text.replace("{" + k + "}", str(v))
    return text.replace("{{", "{").replace("}}", "}")


def child_env(u_id: str, phase: str) -> dict:
    env = {k: v for k, v in os.environ.items()
           if not (k.startswith("CLAUDE") and k != "CLAUDE_CODE_GIT_BASH_PATH")}
    env.update({"CAMERA_LOOP": "1", "LOOP_UNIT": u_id, "LOOP_PHASE": phase, "PYTHONIOENCODING": "utf-8"})
    return env


def kill_tree(pid: int) -> None:
    subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True)


def run_process(cmd: list[str], prompt: str, logfile: Path, timeout_s: float, env: dict) -> tuple[int, str]:
    """Run one headless session, prompt on stdin, output to logfile. Returns (exit code, log tail)."""
    logfile.parent.mkdir(parents=True, exist_ok=True)
    with open(logfile, "wb") as out:
        p = subprocess.Popen(cmd, cwd=ROOT, stdin=subprocess.PIPE, stdout=out, stderr=subprocess.STDOUT, env=env)
        PID_FILE.write_text(str(p.pid))
        try:
            p.stdin.write(prompt.encode("utf-8"))
            p.stdin.close()
        except Exception:
            pass
        awake = 0.0
        last = time.time()
        while p.poll() is None:
            time.sleep(10)
            now_s = time.time()
            gap, last = now_s - last, now_s
            if gap > SLEEP_GAP_S:
                log(f"Windows was asleep for {gap / 60:.0f} min during this session; that time does not count "
                    f"against its {timeout_s / 3600:.1f} h")
                continue
            awake += gap
            if awake > timeout_s:
                log(f"timeout after {timeout_s / 3600:.1f} h awake: killing pid {p.pid}")
                kill_tree(p.pid)
                break
        code = p.wait()
    PID_FILE.unlink(missing_ok=True)
    data = logfile.read_bytes()[-20000:].decode("utf-8", errors="replace")
    return code, data


def claude_session(u: Unit, phase: str, prompt: str, logfile: Path) -> tuple[int, str]:
    cmd = [CLAUDE, "-p", "--dangerously-skip-permissions", "--chrome", "--output-format", "stream-json",
           "--verbose", "--disallowedTools", DISALLOWED]
    return run_process(cmd, prompt, logfile, HOURS.get(phase, 4) * 3600, child_env(u.id, phase))


def rate_limit_info(log_text: str) -> dict:
    """The newest rate-limit snapshot a stream-json log reports ({} if it has none)."""
    for line in reversed(log_text.splitlines()):
        if '"rate_limit_event"' in line:
            try:
                return json.loads(line).get("rate_limit_info") or {}
            except Exception:
                continue
    return {}


def usage_limit(log_text: str) -> tuple[bool, float | None]:
    """Did the session stop on a Claude usage limit, and when does it lift (epoch seconds, if it said)?

    Judged from what the session itself reported: its newest rate-limit snapshot is "rejected", or its
    final result is an error that names a limit, or the CLI printed one outside the JSON. Never from a
    mere mention: every stream-json log carries routine `rate_limit_event` lines (14 in one review), and
    matching those made ANY crash, timeout or missing result look like a limit, rerun for up to 30 h."""
    info = rate_limit_info(log_text)
    if info.get("status") == "rejected" and info.get("resetsAt"):
        return True, float(info["resetsAt"])
    for line in reversed(log_text.splitlines()[-200:]):
        try:
            e = json.loads(line)
        except Exception:
            if LIMIT_RE.search(line):
                return True, None
            continue
        if isinstance(e, dict) and e.get("type") == "result":
            return bool(e.get("is_error") and LIMIT_RE.search(str(e.get("result", "")))), None
    return info.get("status") == "rejected", None


def wait_out_limit(u: Unit, what: str, reset: float | None) -> tuple[bool, float]:
    """Sleep through a usage limit: until the reset time it named (plus a margin), else LIMIT_WAIT_S.
    Returns (stopped by Alexv, seconds waited)."""
    wait = max(60.0, reset - time.time() + 90) if reset else float(LIMIT_WAIT_S)
    until = dt.datetime.fromtimestamp(time.time() + wait).strftime("%a %d %b %H:%M")
    log(f"{u.id}: usage limit hit ({what}); waiting until {until}")
    report(f"{u.id}: paused for a Claude usage limit ({what}) until {until}")
    if wait > 3 * 3600:
        notify(f"loop paused by your Claude usage limit until {until}; it resumes by itself")
    end = time.time() + wait
    while time.time() < end:
        if stop_requested():
            return True, wait
        time.sleep(min(30.0, max(1.0, end - time.time())))
    return False, wait


def weekly_budget() -> float:
    try:
        return float(config().get("weekly_budget", WEEKLY_BUDGET))
    except Exception:
        return WEEKLY_BUDGET


def newest_usage() -> dict:
    """The newest rate-limit snapshot any session reported: the loop's view of Alexv's allowance."""
    for f in sorted(LOGS.glob("*.jsonl"), key=lambda p: p.stat().st_mtime, reverse=True)[:6]:
        info = rate_limit_info(f.read_text(encoding="utf-8", errors="replace"))
        if info:
            return info
    return {}


def weekly_usage() -> tuple[float | None, float | None]:
    """(share of the 7-day allowance used, when that window resets), from the newest snapshot."""
    info = newest_usage()
    w = (info.get("unifiedWindows") or {}).get("seven_day") or {}
    if not w and info.get("rateLimitType") == "seven_day":
        w = info
    used, reset = w.get("utilization"), w.get("resetsAt")
    return (float(used) if used is not None else None), (float(reset) if reset else None)


def budget_pause(state: dict) -> bool:
    """Between phases: once the 7-day allowance is past Alexv's budget, sleep until it resets, so he
    keeps the rest for himself. Re-reads the budget while waiting (raise it and the loop goes on).
    Returns True if Alexv stopped the loop meanwhile."""
    used, reset = weekly_usage()
    if used is None or not reset or used < weekly_budget() or time.time() >= reset:
        return False
    until = dt.datetime.fromtimestamp(reset + 120).strftime("%a %d %b %H:%M")
    if state.get("budget_pause") != reset:
        state["budget_pause"] = reset
        save_state(state)
        log(f"BUDGET: weekly Claude allowance {used:.0%} used (budget {weekly_budget():.0%}); pausing until {until}")
        report(f"**paused** — your weekly Claude allowance is {used:.0%} used; the loop leaves you the rest "
               f"and resumes {until}")
        notify(f"loop paused: your weekly Claude allowance is {used:.0%} used, so it leaves you the rest and "
               f"resumes {until}. To let it use more, set weekly_budget in loop/runtime/config.local.json")
    keep_awake(False)   # nothing to do for days: let the PC sleep; it resumes once awake after the reset
    try:
        while time.time() < reset + 120 and used >= weekly_budget():
            if stop_requested():
                return True
            time.sleep(30)
    finally:
        keep_awake(True)
    state.pop("budget_pause", None)
    log("BUDGET: resuming")
    return False


def read_result(path: Path) -> dict | None:
    if not path.exists():
        return None
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        return data if data.get("status") in ("ok", "blocked", "failed") else None
    except Exception:
        return None


def run_phase(u: Unit, us: dict, phase: str, state: dict) -> dict:
    """One attempt of one phase, retried in place across usage limits. Returns the result dict."""
    attempt = us["attempts"].get(phase, 0) + 1
    story_file = rel(IMPL / f"{u.id}.md")
    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    result_path = RESULTS / u.id / f"{phase}-a{attempt}-{stamp}.json"
    logfile = LOGS / f"{stamp}-{u.id}-{phase}-a{attempt}.jsonl"
    hint = us.get("hint") or ""
    ctx = {
        "unit": u.id, "epic": u.epic, "attempt": attempt, "branch": u.branch, "story_file": story_file,
        "base_ref": us.get("base_ref") or "master", "result_path": rel(result_path),
        "findings_path": us.get("findings") or "",
        "previous_failure": (("" if hint.startswith("### ") else "### The previous attempt did not finish\n\n") +
                             f"{hint}\n\nCheck what it committed "
                             f"(`git log {us.get('base_ref') or 'master'}..HEAD`) before redoing anything.") if hint else "",
    }
    if phase in NEEDS_UNITY:
        ensure_unity()
    waited = 0
    cuts = 0
    while True:
        started = time.time()
        log(f"{u.id}: {phase} attempt {attempt} → {rel(logfile)}")
        code, tail = claude_session(u, phase, render(phase, ctx), logfile)
        res = read_result(result_path)
        mins = (time.time() - started) / 60
        hit, reset = usage_limit(logfile.read_text(encoding="utf-8", errors="replace")) if res is None else (False, None)
        if hit and waited < (LIMIT_RESET_MAX_S if reset else LIMIT_WAIT_MAX_S):
            stopped, slept = wait_out_limit(u, f"{phase} after {mins:.0f} min", reset)
            if stopped:
                return {"status": "stopped"}
            waited += slept
            cuts += 1
            # The cut-off session may have committed half the work: the next one must know, and the
            # log of what it did must survive (the retry used to overwrite it).
            ctx["previous_failure"] = (
                (ctx["previous_failure"] + "\n\n" if hint else "") +
                f"### This attempt was cut off by a Claude usage limit\n\nThe previous session of this same "
                f"attempt stopped after {mins:.0f} min, when the usage limit was reached (its log: "
                f"{rel(logfile)}). It may have committed part of the work or left changes uncommitted. Run "
                f"`git log {us.get('base_ref') or 'master'}..HEAD` and `git status` first, carry on from where "
                f"it stopped, and do not redo what is already done.")
            logfile = LOGS / f"{stamp}-{u.id}-{phase}-a{attempt}-r{cuts + 1}.jsonl"
            continue
        if res is None:
            res = {"status": "failed", "summary": f"no valid result file (exit {code}, {mins:.0f} min)",
                   "next_hint": f"The previous session ended without writing its result file (exit code {code}). "
                                f"It probably crashed, timed out or ran out of context. Its log: {rel(logfile)}"}
        res["_minutes"] = round(mins)
        res["_log"] = rel(logfile)
        res["_attempt"] = attempt
        return res


def final_message(log_text: str) -> str:
    """The session's last answer, from the `result` event of a stream-json log ('' if none)."""
    for line in reversed(log_text.splitlines()):
        try:
            e = json.loads(line)
        except Exception:
            continue
        if e.get("type") == "result":
            return "" if e.get("is_error") else str(e.get("result") or "")
    return ""


def tool_output_text(log_text: str) -> str:
    """Everything the session's tools returned to it (file contents, command output), as one string."""
    out = []
    for line in log_text.splitlines():
        try:
            e = json.loads(line)
        except Exception:
            continue
        content = (e.get("message") or {}).get("content") if e.get("type") == "user" else None
        for c in content if isinstance(content, list) else []:
            if isinstance(c, dict) and c.get("type") == "tool_result":
                r = c.get("content")
                if isinstance(r, list):
                    out.extend(x.get("text", "") for x in r if isinstance(x, dict))
                else:
                    out.append(str(r or ""))
    return "\n".join(out)


def is_rig(path: str, text: str) -> bool:
    """A verification rig (a tool the review may run, never a subject of it)?"""
    if REVIEW_RIG_RE.search(path):
        return True
    return bool(REVIEW_RIG_RUNTIME_RE.search(path)) and text.lstrip("﻿ \t\r\n").startswith("#if UNITY_EDITOR")


def review_sources(base_ref: str, branch: str) -> dict[str, str]:
    """The changed source files a cross-review must see, rigs left out: {path: text at the branch}."""
    names = git("diff", "--name-only", "--diff-filter=d", f"{base_ref}..{branch}", check=False).stdout.splitlines()
    files = {p: git("show", f"{branch}:{p}", check=False).stdout for p in names if REVIEW_SOURCE_RE.search(p)}
    return {p: t for p, t in files.items() if not is_rig(p, t)}


GAME_PATH_RE = re.compile(r"^(?:Assets|ProjectSettings|Packages)/")


def game_changes(old: str, new: str) -> list[str]:
    """The files a commit range changed in the game itself (Assets, ProjectSettings, Packages), rigs and
    their .meta left out: what a player would get, as opposed to docs, evidence and verification tools."""
    if not old or not new or old == new:
        return []
    out = []
    for p in git("diff", "--name-only", f"{old}..{new}", check=False).stdout.splitlines():
        if not GAME_PATH_RE.search(p):
            continue
        src = p[:-5] if p.endswith(".meta") else p
        text = git("show", f"{new}:{src}", check=False).stdout if REVIEW_RIG_RUNTIME_RE.search(src) else ""
        if not is_rig(src, text):
            out.append(p)
    return out


def _distinctive(lines) -> set[str]:
    return {s for s in (l.strip() for l in lines) if len(s) > 20}


def review_coverage(base_ref: str, branch: str, log_text: str) -> tuple[list[str], list[str]]:
    """Which changed source files did the reviewer actually see? Measured, never self-reported. A file
    counts as read when most of its distinctive lines (over 20 characters) appear in what its tools
    returned (a full read, by Read, cat or git show), or when nearly all of its distinctive CHANGED
    lines do (a diff review: on 2026-11-11 a reviewer diffed four comment-only files and was wrongly
    re-asked). Output cut off by a tool limit does not count, because the reviewer never saw it
    either: that is how the 2026-11-10 rubber stamp missed half its files. Returns (read, unread)."""
    seen = tool_output_text(log_text)
    share = lambda lines: sum(l in seen for l in lines) / len(lines)
    read, unread = [], []
    for p, text in review_sources(base_ref, branch).items():
        lines = _distinctive(text.splitlines())
        if not lines:
            continue
        diff = git("diff", f"{base_ref}..{branch}", "--", p, check=False).stdout.splitlines()
        changes = (_distinctive(l[1:] for l in diff if l.startswith("+") and not l.startswith("+++"))
                   or _distinctive(l[1:] for l in diff if l.startswith("-") and not l.startswith("---")))
        ok = share(lines) >= REVIEW_FILE_SEEN or bool(changes) and share(changes) >= REVIEW_CHANGE_SEEN
        (read if ok else unread).append(p)
    return read, unread


def run_cross_review(u: Unit, us: dict) -> str | None:
    """A second, READ-ONLY Claude Code session on a different model reviews the story's diff.

    Independence comes from three things: a fresh context that never saw the code being written, a
    different model (REVIEW_MODEL) with different blind spots, and a brief that asks for failure
    scenarios, not opinions. It cannot change anything: editing tools and the Unity/Blender/Chrome
    servers are switched off for it, the guard refuses git writes in this phase, and any tracked
    change it still manages to make is reverted below. Its findings are its final message.
    Returns the findings path, or None if the review could not run (never a blocker)."""
    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    findings = RESULTS / u.id / f"cross-review-findings-{stamp}.md"
    findings.parent.mkdir(parents=True, exist_ok=True)
    ctx = {"unit": u.id, "story_file": rel(IMPL / f"{u.id}.md"), "base_ref": us["base_ref"], "branch": u.branch,
           "coverage_hint": ""}
    cmd = [CLAUDE, "-p", "--dangerously-skip-permissions", "--model", REVIEW_MODEL, "--effort", REVIEW_EFFORT,
           "--output-format", "stream-json", "--verbose", "--disallowedTools", REVIEW_DISALLOWED]
    # Remember what was ALREADY changed, so the backstop below undoes only the reviewer's own edits.
    # (Measured 2026-11-10: a blanket `git checkout -- .` here reverted an unrelated uncommitted edit.)
    before = {p: (ROOT / p).read_bytes() for p in dirty_tracked() if (ROOT / p).is_file()}
    answer, code, coverage, stopped = "", 0, "", False
    us.pop("review_coverage", None)
    for attempt in (1, 2):   # a verdict from a reviewer that did not read the code is re-asked once
        logfile = LOGS / f"{stamp}-{u.id}-cross-review{'' if attempt == 1 else '-a2'}.jsonl"
        log(f"{u.id}: cross-review by a read-only Claude session ({REVIEW_MODEL}, effort {REVIEW_EFFORT}) "
            f"→ {rel(logfile)}")
        waited = 0
        while True:
            code, _ = run_process(cmd, render("cross-review", ctx, with_rules=False), logfile,
                                  HOURS["cross-review"] * 3600, child_env(u.id, "cross-review"))
            text = logfile.read_text(encoding="utf-8", errors="replace")
            answer = final_message(text)
            # Checked BEFORE the answer is trusted: a session cut off by a limit "answers" with the
            # limit message itself, which would be saved as a review with 0 findings.
            hit, reset = usage_limit(text)
            if hit:
                answer = ""
                if waited < (LIMIT_RESET_MAX_S if reset else LIMIT_WAIT_MAX_S):
                    stopped, slept = wait_out_limit(u, "cross-review", reset)
                    if stopped:
                        break
                    waited += slept
                    continue
            break
        if stopped or not answer.strip():
            break
        read, unread = review_coverage(us["base_ref"], u.branch, text)
        total = len(read) + len(unread)
        coverage = f"read {len(read)}/{total} changed source files (measured from its tool output)"
        if not total or len(read) >= REVIEW_MIN_COVERAGE * total:
            break
        if attempt == 1:
            report(f"{u.id}: cross-review answered after seeing only {len(read)}/{total} changed source files; "
                   f"asked again")
            ctx["coverage_hint"] = (
                "## Your previous attempt was not accepted\n\n"
                f"It gave a verdict after its tools had shown it only {len(read)} of the {total} changed source "
                "files. Read each of these **in full** before you answer. Use the Read tool: shell output is cut "
                "off after about 30,000 characters, and what was cut off was never seen.\n\n"
                + "\n".join(f"- `{p}`" for p in unread) + "\n")
        else:
            answer = (f"COVERAGE WARNING: even when asked twice, the reviewer saw only {len(read)} of {total} changed "
                      f"source files. Unread: {', '.join(unread)}. Treat the review below as partial.\n\n" + answer)
            report(f"{u.id}: cross-review still saw only {len(read)}/{total} files; its review is marked partial")
    undone = []
    for p in dirty_tracked():
        if p in before:
            if (ROOT / p).is_file() and (ROOT / p).read_bytes() != before[p]:
                (ROOT / p).write_bytes(before[p])
                undone.append(p)
        else:
            git("checkout", "--", p, check=False)
            undone.append(p)
    if undone:
        report(f"{u.id}: the read-only reviewer changed {', '.join(undone[:6])}; undone")
    if stopped:
        return None
    if not answer.strip():
        report(f"{u.id}: cross-review produced no findings text (exit {code}); skipped, see {rel(logfile)}")
        return None
    us["review_coverage"] = coverage
    findings.write_text(answer.rstrip() + (f"\n\n---\nCoverage: {coverage}\n" if coverage else "\n"), encoding="utf-8")
    return rel(findings)


def findings_count(path: str) -> int:
    text = (ROOT / path).read_text(encoding="utf-8")
    m = re.search(r"FINDINGS:\s*(\d+)", text)
    if m:
        return int(m.group(1))
    return len(re.findall(r"^\s*\d+\.\s*\[(high|medium|low)\]", text, re.M | re.I))


# ----------------------------------------------------------------------------- one unit, start to merge

def park(u: Unit, us: dict, state: dict, phase: str, reason: str) -> None:
    us["parked"] = {"reason": reason, "phase": phase, "since": now()}
    save_state(state)
    report(f"**PARKED {u.id}** at `{phase}`: {reason}")
    notify(f"parked {u.id} ({phase}): {reason}")


def start_branch(u: Unit, us: dict) -> None:
    if current_branch() != "master":
        commit_tracked_leftovers(f"loop: leftovers on {current_branch()} before switching")
        git("checkout", "master")
    if commit_tracked_leftovers("loop: editor-side changes found on master between units"):
        report("committed editor-side changes found on master (see git log)")
    sync_master()
    if branch_exists(u.branch):
        git("checkout", u.branch)
        # A resumed branch (after a stop, or a park that was cleared) must pick up what master gained
        # meanwhile: loop fixes, prompt changes, lessons, other stories. Re-base the review diff on the
        # new merge-base so reviewers still see only this unit's own changes.
        if git("merge-base", "--is-ancestor", "master", u.branch, check=False).returncode != 0:
            r = git("merge", "--no-ff", "master", "-m", f"loop: bring master into {u.branch}\n\n{ATTRIBUTION}",
                    check=False)
            if r.returncode != 0:
                git("merge", "--abort", check=False)
                report(f"{u.id}: could not bring master into its branch (conflict); continuing on the old base")
            else:
                us["base_ref"] = git("merge-base", "master", "HEAD").stdout.strip()
                report(f"{u.id}: brought master into its branch before resuming")
        return
    tag = f"loop/pre-{u.id}"
    if git("rev-parse", "--verify", "--quiet", f"refs/tags/{tag}", check=False).returncode != 0:
        git("tag", tag)
        push(tag)
    us["base_ref"] = git("rev-parse", "HEAD").stdout.strip()
    git("checkout", "-b", u.branch)


def finish_phase_git(u: Unit, phase: str) -> None:
    if current_branch() != u.branch:   # the guard should make this impossible; recover anyway
        log(f"WARNING: session left the repo on {current_branch()}; returning to {u.branch}")
        commit_tracked_leftovers(f"loop: leftovers from {u.id} {phase} on the wrong branch")
        git("checkout", u.branch)
    if commit_tracked_leftovers(f"loop: uncommitted changes left by the {phase} phase of {u.id}"):
        report(f"{u.id} {phase}: session left tracked changes uncommitted; the loop committed them")
    stray = untracked_project_files()
    if stray:
        report(f"{u.id} {phase}: untracked files left behind: {', '.join(stray[:8])}")
    push(u.branch)   # backup of the work in progress; never master from here


def next_phase(u: Unit, us: dict, phase: str) -> str:
    if phase == "cross-review":
        return "fix" if us.get("findings") and findings_count(us["findings"]) > 0 else "close"
    seq = u.phases
    i = seq.index(phase)
    return seq[i + 1] if i + 1 < len(seq) else "merge"


def merge_conflicts(branch: str) -> list[str]:
    """The paths a merge of `branch` into master would REALLY conflict on, worked out by git merge-tree
    without touching the working tree. Empty when it merges cleanly (or merge-tree itself failed)."""
    r = git("merge-tree", "--write-tree", "--name-only", "master", branch, check=False)
    if r.returncode != 1:
        return []
    return list(itertools.takewhile(bool, r.stdout.splitlines()[1:]))


def remove_half_merged(branch: str) -> list[str]:
    """A merge that dies halfway leaves the branch's NEW files behind as untracked copies, and those then
    block every later checkout or merge. Remove exactly those: untracked files byte-identical to the
    branch's own version of the same path (nobody's work is lost: the branch still has them)."""
    out = git("status", "--porcelain", "-z", "--untracked-files=all", check=False).stdout
    removed = []
    for entry in filter(None, out.split("\0")):
        p = entry[3:]
        if not entry.startswith("?? "):
            continue
        theirs = git("rev-parse", "--verify", "--quiet", f"{branch}:{p}", check=False).stdout.strip()
        if theirs and theirs == git("hash-object", "--", p, check=False).stdout.strip():
            (ROOT / p).unlink(missing_ok=True)
            removed.append(p)
            d = (ROOT / p).parent
            while d != ROOT and d.is_dir() and not any(d.iterdir()):   # and the folders they emptied
                d.rmdir()
                d = d.parent
    return removed


def merge_unit(u: Unit, us: dict, state: dict) -> bool:
    git("checkout", "master")
    sync_master()
    conflicts = merge_conflicts(u.branch)
    if conflicts:
        park(u, us, state, "merge", f"merge conflict with master in {', '.join(conflicts[:5])}; resolve by hand "
                                    "or ask Claude to rebase the branch")
        return False
    # Not a conflict, so a failure here is the machine: on 2026-11-11 a merge that git merge-tree called
    # clean died halfway (most likely a file held by Unity, OneDrive or a virus scan right after the
    # checkout), was reported as a "conflict", and stranded the branch's new files on master.
    summary = (us["history"][-1]["summary"] if us["history"] else "")[:200]
    err = ""
    for attempt in range(1, MERGE_TRIES + 1):
        r = git("merge", "--no-ff", u.branch, "-m", f"loop: merge {u.id}\n\n{summary}\n\n{ATTRIBUTION}", check=False)
        if r.returncode == 0:
            break
        err = " ".join((r.stderr.strip() or r.stdout.strip()).split())
        git("merge", "--abort", check=False)
        stray = remove_half_merged(u.branch)
        log(f"{u.id}: merge attempt {attempt}/{MERGE_TRIES} failed, not a conflict: {err[:400]}"
            + (f" (removed {len(stray)} half-merged file(s))" if stray else ""))
        if attempt < MERGE_TRIES:
            time.sleep(MERGE_RETRY_S)
    else:
        park(u, us, state, "merge", f"merge failed {MERGE_TRIES} times although it has no conflicts (a locked file? "
                                    f"Unity, OneDrive or a virus scan): {err[:240]}")
        return False
    us["merged"] = True
    if us.get("lessons"):
        with open(LESSONS_FILE, "a", encoding="utf-8") as f:
            f.write(f"\n## From {u.id} ({dt.date.today().isoformat()})\n\n")
            f.writelines(f"- {l}\n" for l in us["lessons"])
        git("add", rel(LESSONS_FILE))
        git("commit", "-m", f"loop: lessons from {u.id}\n\n{ATTRIBUTION}")
    push("master")
    save_state(state)
    report(f"**MERGED {u.id}** to master" +
           (f" — {len(us['human_checks'])} check(s) wait for Alexv" if us["human_checks"] else ""))
    return True


def absorb_result(us: dict, res: dict) -> None:
    """Fold one phase's result file into the unit's running record."""
    us["decisions"] += res.get("decisions") or []   # new decisions only, per _rules.md
    # Each phase reports the unit's COMPLETE list of open checks (_rules.md), so the newest list
    # replaces the old one. Appending gave Alexv every check three times (Story 1.14: 12 for 4).
    # An empty list never clears them: only Alexv closes a check.
    if res.get("human_checks"):
        us["human_checks"] = list(res["human_checks"])
    us["lessons"] += [l for l in res.get("lessons") or [] if l not in us["lessons"]]


def run_unit(u: Unit, state: dict) -> str:
    """Drive one unit to merged / parked / stopped. Returns that outcome."""
    us = unit_state(state, u)
    if us.get("merged"):
        return "merged"
    if u.kind == "story" and us["phase"] == "create" and (IMPL / f"{u.id}.md").exists():
        us["phase"] = "dev"     # the story file already exists: nothing to create
    start_branch(u, us)
    if not us.get("base_ref"):
        us["base_ref"] = git("merge-base", "master", u.branch).stdout.strip()
    save_state(state)
    report(f"start {u.kind} **{u.id}** at `{us['phase']}` on `{u.branch}`")

    while us["phase"] != "merge":
        if code_changed():   # e.g. start_branch just brought master's loop fixes into this branch
            save_state(state)
            return "reload"
        if stop_requested() or budget_pause(state):
            return "stopped"
        phase = us["phase"]
        if phase == "cross-review":
            us["findings"] = run_cross_review(u, us)
            n = findings_count(us["findings"]) if us["findings"] else 0
            us["history"].append({"phase": phase, "status": "ok", "summary": f"cross-review: {n} finding(s)", "at": now()})
            report(f"{u.id} cross-review ({REVIEW_MODEL}, read-only): {n} finding(s); "
                   f"{us.get('review_coverage') or 'coverage not measured'}")
            us["phase"] = next_phase(u, us, phase)
            save_state(state)
            continue

        tip = git("rev-parse", u.branch, check=False).stdout.strip()
        res = run_phase(u, us, phase, state)
        if res["status"] == "stopped":
            return "stopped"
        finish_phase_git(u, phase)
        new_tip = git("rev-parse", u.branch, check=False).stdout.strip()
        progressed = new_tip != tip
        us["history"].append({"phase": phase, "status": res["status"], "summary": res.get("summary", ""),
                              "minutes": res.get("_minutes"), "log": res.get("_log"), "at": now()})
        absorb_result(us, res)
        report(f"{u.id} {phase} a{res.get('_attempt')} → **{res['status']}** ({res.get('_minutes')} min): "
               f"{res.get('summary', '')[:300]}")

        built = game_changes(tip, new_tip) if res["status"] == "ok" and phase == "close" and progressed else []
        if built:
            # A close never certifies its own build. Story 2.2's first close failed AC3 and handed its
            # retry a plan to build a rock edge round the whole ring: that retry would have built it,
            # judged it and merged it, with no other session ever looking at it.
            us["attempts"][phase] = runs = us["attempts"].get(phase, 0) + 1
            if runs >= MAX_RUNS:
                park(u, us, state, phase, f"close passed its own changes to the game {runs} times running "
                                          f"instead of only verifying them; last: {', '.join(built[:3])}")
                git("checkout", "master")
                return "parked"
            us["hint"] = (
                "### Verify what the previous close built\n\n"
                f"The previous close attempt passed, but it changed the game itself ({len(built)} file(s): "
                f"{', '.join(built[:12])}{' …' if len(built) > 12 else ''}). A close never certifies its "
                "own build, so you are that work's independent check. Read those changes as a reviewer "
                "would, run what exercises them (the story's rigs, the scene, the tests) and look at the "
                "evidence yourself, not at its summary. Then run the six checks. Its result said: "
                f"{res.get('summary', '')[:600]}")
            log(f"{u.id}: close attempt {res.get('_attempt')} passed its own changes to the game "
                f"({len(built)} file(s)); a fresh close verifies them")
            save_state(state)
            continue

        if res["status"] == "ok":
            state["unity_restarts"] = 0
            us["hint"] = ""
            us["phase"] = next_phase(u, us, phase)
        elif res["status"] == "blocked":
            reason = res.get("blocker") or "no reason given"
            if reason.upper().startswith("UNITY:"):
                state["unity_restarts"] = state.get("unity_restarts", 0) + 1
                if state["unity_restarts"] > MAX_UNITY_RESTARTS:
                    save_state(state)
                    notify(f"loop stopped: Unity unreachable after {MAX_UNITY_RESTARTS} restarts ({u.id})", urgent=True)
                    return "infra-stop"
                restart_unity()
                us["hint"] = f"The previous attempt could not reach Unity ({reason}); the editor was restarted."
            else:
                park(u, us, state, phase, reason)
                git("checkout", "master")
                return "parked"
        else:  # failed
            # "failed" covers two different things: a session that got nowhere, and one that ran out of
            # context halfway through a big story and handed over (as _rules.md tells it to). Only the
            # first counts towards parking; a handover is judged by what it committed. (Story 2.2's first
            # dev session did 7 of 13 tasks in 12 commits and was counted exactly like a dud.)
            us["attempts"][phase] = runs = us["attempts"].get(phase, 0) + 1
            stalls = us.setdefault("stalls", {})
            if not progressed:
                stalls[phase] = stalls.get(phase, 0) + 1
            us["hint"] = res.get("next_hint") or res.get("summary") or ""
            if stalls.get(phase, 0) >= MAX_ATTEMPTS or runs >= MAX_RUNS:
                park(u, us, state, phase, f"failed {runs} times, {stalls.get(phase, 0)} of them with nothing "
                                          f"committed; last: {us['hint'][:160]}")
                git("checkout", "master")
                return "parked"
            if progressed:
                log(f"{u.id}: {phase} attempt {runs} handed over with new commits; continuing "
                    f"(run {runs} of at most {MAX_RUNS})")
        save_state(state)

    return "merged" if merge_unit(u, us, state) else "parked"


# ----------------------------------------------------------------------------- entry points

def smoke(state: dict) -> int:
    u = Unit("story", "smoke", 0)
    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    result_path = RESULTS / "smoke" / f"smoke-{stamp}.json"
    try:
        ensure_unity()
    except InfraStop as e:
        print(f"SMOKE STOPPED BEFORE THE SESSION: {e}")
        return 2
    prompt = (PROMPTS / "smoke.md").read_text(encoding="utf-8").replace("{result_path}", rel(result_path))
    code, tail = claude_session(u, "smoke", prompt, LOGS / f"{stamp}-smoke.jsonl")
    res = read_result(result_path)
    print(json.dumps(res, indent=2) if res else f"NO RESULT (exit {code}). Log tail:\n{tail[-3000:]}")
    return 0 if res and res.get("status") == "ok" else 1


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", help="run just this unit id (story key, plan-epic-N, retro-epic-N), then stop")
    ap.add_argument("--max-units", type=int, default=0)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--smoke", action="store_true")
    args = ap.parse_args()

    for d in (RUNTIME, LOGS, RESULTS, REPORTS):
        d.mkdir(parents=True, exist_ok=True)
    state = load_state()
    cfg = config()

    if args.dry_run:
        explain: list = []
        gated: list = []
        u = pick_next(load_sprint(), load_deps(), state, explain, gated)
        print("next unit:", u if u else ("none: idle until Alexv approves " +
                                         ", ".join(sorted({str(g['id']) for _, g in gated})) if gated else None))
        print("\n".join("  skipped: " + e for e in explain) or "  (nothing skipped)")
        return 0
    if args.smoke:
        return smoke(state)

    keep_awake()
    STOP_FILE.unlink(missing_ok=True)
    log(f"loop started (ntfy topic in {rel(CONFIG_FILE)})")
    report("**loop started**")
    done = 0
    try:
        while True:
            if stop_requested():
                log("STOP file found: stopping cleanly")
                report("stopped on request")
                return 0
            if code_changed():   # a merge to master, or a checkout of it, brought new loop code
                log("loop code changed on disk: restarting on the new code")
                return RELOAD
            sprint, deps = load_sprint(), load_deps()
            gated: list = []
            u = unit_from_id(args.only) if args.only else pick_next(sprint, deps, state, gated=gated)
            told = state.setdefault("gates_notified", [])
            for uid, g in gated:   # once per gate, the first time it is what actually holds a unit back
                if g["id"] not in told:
                    told.append(g["id"])
                    log(f"GATE: {uid} waits for Alexv's approval of {g['id']}")
                    notify(f"{uid} needs your approval of {g['id']} before it can start. The loop does other "
                           f"work meanwhile. See section 0 of loop/runtime/human-queue.md", urgent=True)
                    save_state(state)
            if u is None and gated:
                ids = sorted({str(g["id"]) for _, g in gated})
                if "idle" not in state or state["idle"] != ids:
                    state["idle"] = ids
                    save_state(state)
                    log(f"idle: nothing else to do until Alexv approves {', '.join(ids)}")
                    report(f"**idle** — waiting for your approval of {', '.join(ids)}")
                    notify(f"loop idle: nothing else it can do until you approve {', '.join(ids)}", urgent=True)
                keep_awake(False)   # waiting for a human: let the PC sleep; work resumes when it wakes
                for _ in range(GATE_POLL_S // 30):
                    if stop_requested() or not (approvals().isdisjoint(i.upper() for i in ids)):
                        break
                    time.sleep(30)
                keep_awake(True)
                continue
            state.pop("idle", None)
            if u is None:
                n_parked = sum(1 for v in state["units"].values() if v.get("parked"))
                msg = (f"loop finished: nothing left it can do. {n_parked} item(s) parked; "
                       f"see loop/runtime/human-queue.md")
                report(f"**{msg}**")
                notify(msg, urgent=True)
                return 0
            try:
                outcome = run_unit(u, state)
            except InfraStop as e:
                save_state(state)
                report(f"**STOPPED — machine problem**: {e}")
                notify(f"loop stopped: {e}"[:200], urgent=True)
                return 2
            if outcome == "reload":
                log(f"{u.id}: loop code changed on disk (master brought in): restarting on the new code")
                return RELOAD
            log(f"{u.id}: {outcome}")
            if outcome == "infra-stop":
                return 2
            if outcome == "stopped":
                report("stopped on request")
                return 0
            done += 1
            if args.only or (args.max_units and done >= args.max_units):
                report(f"stopping after {done} unit(s) as asked")
                notify(f"loop paused after {u.id}: {outcome}")
                return 0
    except Exception as e:
        log("CRASH:\n" + traceback.format_exc())
        report(f"**loop crashed**: {e}")
        notify(f"loop crashed: {str(e)[:150]}", urgent=True)
        return 1
    finally:
        save_state(state)


def relaunch(argv: list[str]) -> int:
    """Run the loop again in a fresh process, so it loads the loop.py now on disk, and wait for it.
    A child process rather than os.execv: on Windows exec ends this process, and the 'Game loop' window
    would print 'Loop exited' while the new loop is still running in it."""
    keep_awake(False)   # the new process keeps the PC awake itself; this one only waits
    return subprocess.call([sys.executable, str(CODE_FILE), *argv], cwd=ROOT)


if __name__ == "__main__":
    code = main()
    sys.exit(relaunch(sys.argv[1:]) if code == RELOAD else code)
