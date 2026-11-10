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
PID_FILE = RUNTIME / "current.pid"
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
MAX_ATTEMPTS = 3
MAX_UNITY_RESTARTS = 2          # consecutive, before the whole loop stops
LIMIT_WAIT_S = 20 * 60          # usage limit: wait, then retry the same attempt
LIMIT_WAIT_MAX_S = 30 * 3600
LIMIT_RE = re.compile(r"usage limit|limit reached|hit your limit|rate.?limit|overloaded|"
                      r"too many requests|\b429\b|\b529\b|credit balance", re.I)
DISALLOWED = "AskUserQuestion,Workflow,EnterPlanMode,ExitPlanMode"
REVIEW_DISALLOWED = DISALLOWED + ",Edit,Write,NotebookEdit,mcp__UnityMCP,mcp__blender,mcp__claude-in-chrome"


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


def keep_awake() -> None:
    """Stop Windows sleeping while the loop runs, without changing any power setting."""
    try:
        ctypes.windll.kernel32.SetThreadExecutionState(0x80000000 | 0x00000001)  # CONTINUOUS|SYSTEM
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
    r = subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
                       errors="replace")
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


def pick_next(sprint: dict, deps: dict, state: dict, explain: list | None = None) -> Unit | None:
    def why(msg):
        if explain is not None:
            explain.append(msg)

    order = deps["epic_order"]
    epic_deps = {int(k): v for k, v in (deps.get("epic_depends_on") or {}).items()}
    story_deps = deps.get("story_depends_on") or {}

    for e in order:   # 1. a finished epic gets its retrospective first
        if epic_satisfied(sprint, state, e) and sprint.get(f"epic-{e}-retrospective") != "done":
            uid = f"retro-epic-{e}"
            if not parked(state, uid) and not state["units"].get(uid, {}).get("merged"):
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
             "Regenerated by `loop/loop.py` after every phase. Three kinds of item, in order of urgency.", ""]
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
        deadline = time.time() + timeout_s
        while p.poll() is None:
            if time.time() > deadline:
                log(f"timeout after {timeout_s / 3600:.1f} h: killing pid {p.pid}")
                kill_tree(p.pid)
                break
            time.sleep(10)
        code = p.wait()
    PID_FILE.unlink(missing_ok=True)
    data = logfile.read_bytes()[-20000:].decode("utf-8", errors="replace")
    return code, data


def claude_session(u: Unit, phase: str, prompt: str, logfile: Path) -> tuple[int, str]:
    cmd = [CLAUDE, "-p", "--dangerously-skip-permissions", "--chrome", "--output-format", "stream-json",
           "--verbose", "--disallowedTools", DISALLOWED]
    return run_process(cmd, prompt, logfile, HOURS.get(phase, 4) * 3600, child_env(u.id, phase))


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
        "previous_failure": (f"### The previous attempt did not finish\n\n{hint}\n\nCheck what it committed "
                             f"(`git log {us.get('base_ref') or 'master'}..HEAD`) before redoing anything.") if hint else "",
    }
    if phase in NEEDS_UNITY:
        ensure_unity()
    waited = 0
    while True:
        started = time.time()
        log(f"{u.id}: {phase} attempt {attempt} → {rel(logfile)}")
        code, tail = claude_session(u, phase, render(phase, ctx), logfile)
        res = read_result(result_path)
        mins = (time.time() - started) / 60
        if res is None and LIMIT_RE.search(tail) and waited < LIMIT_WAIT_MAX_S:
            log(f"{u.id}: usage/rate limit hit after {mins:.0f} min; waiting {LIMIT_WAIT_S // 60} min")
            report(f"{u.id} {phase}: paused for a Claude usage limit")
            for _ in range(LIMIT_WAIT_S // 30):
                if stop_requested():
                    return {"status": "stopped"}
                time.sleep(30)
            waited += LIMIT_WAIT_S
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
    logfile = LOGS / f"{stamp}-{u.id}-cross-review.jsonl"
    ctx = {"unit": u.id, "story_file": rel(IMPL / f"{u.id}.md"), "base_ref": us["base_ref"], "branch": u.branch}
    cmd = [CLAUDE, "-p", "--dangerously-skip-permissions", "--model", REVIEW_MODEL,
           "--output-format", "stream-json", "--verbose", "--disallowedTools", REVIEW_DISALLOWED]
    log(f"{u.id}: cross-review by a read-only Claude session ({REVIEW_MODEL}) → {rel(logfile)}")
    # Remember what was ALREADY changed, so the backstop below undoes only the reviewer's own edits.
    # (Measured 2026-11-10: a blanket `git checkout -- .` here reverted an unrelated uncommitted edit.)
    before = {p: (ROOT / p).read_bytes() for p in dirty_tracked() if (ROOT / p).is_file()}
    waited = 0
    while True:
        code, _ = run_process(cmd, render("cross-review", ctx, with_rules=False), logfile,
                              HOURS["cross-review"] * 3600, child_env(u.id, "cross-review"))
        text = logfile.read_text(encoding="utf-8", errors="replace")
        answer = final_message(text)
        if not answer and LIMIT_RE.search(text[-20000:]) and waited < LIMIT_WAIT_MAX_S:
            log(f"{u.id}: usage/rate limit during cross-review; waiting {LIMIT_WAIT_S // 60} min")
            for _ in range(LIMIT_WAIT_S // 30):
                if stop_requested():
                    return None
                time.sleep(30)
            waited += LIMIT_WAIT_S
            continue
        break
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
    if not answer.strip():
        report(f"{u.id}: cross-review produced no findings text (exit {code}); skipped, see {rel(logfile)}")
        return None
    findings.write_text(answer, encoding="utf-8")
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


def merge_unit(u: Unit, us: dict, state: dict) -> bool:
    git("checkout", "master")
    sync_master()
    summary = (us["history"][-1]["summary"] if us["history"] else "")[:200]
    r = git("merge", "--no-ff", u.branch, "-m", f"loop: merge {u.id}\n\n{summary}\n\n{ATTRIBUTION}", check=False)
    if r.returncode != 0:
        git("merge", "--abort", check=False)
        park(u, us, state, "merge", "merge conflict with master; resolve by hand or ask Claude to rebase the branch")
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
        if stop_requested():
            return "stopped"
        phase = us["phase"]
        if phase == "cross-review":
            us["findings"] = run_cross_review(u, us)
            n = findings_count(us["findings"]) if us["findings"] else 0
            us["history"].append({"phase": phase, "status": "ok", "summary": f"cross-review: {n} finding(s)", "at": now()})
            report(f"{u.id} cross-review ({REVIEW_MODEL}, read-only): {n} finding(s)")
            us["phase"] = next_phase(u, us, phase)
            save_state(state)
            continue

        res = run_phase(u, us, phase, state)
        if res["status"] == "stopped":
            return "stopped"
        finish_phase_git(u, phase)
        us["history"].append({"phase": phase, "status": res["status"], "summary": res.get("summary", ""),
                              "minutes": res.get("_minutes"), "log": res.get("_log"), "at": now()})
        us["decisions"] += res.get("decisions") or []
        us["human_checks"] += res.get("human_checks") or []
        us["lessons"] += res.get("lessons") or []
        report(f"{u.id} {phase} a{res.get('_attempt')} → **{res['status']}** ({res.get('_minutes')} min): "
               f"{res.get('summary', '')[:300]}")

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
            us["attempts"][phase] = us["attempts"].get(phase, 0) + 1
            us["hint"] = res.get("next_hint") or res.get("summary") or ""
            if us["attempts"][phase] >= MAX_ATTEMPTS:
                park(u, us, state, phase, f"failed {MAX_ATTEMPTS} times; last: {us['hint'][:160]}")
                git("checkout", "master")
                return "parked"
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
        u = pick_next(load_sprint(), load_deps(), state, explain)
        print("next unit:", u)
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
            sprint, deps = load_sprint(), load_deps()
            u = unit_from_id(args.only) if args.only else pick_next(sprint, deps, state)
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


if __name__ == "__main__":
    sys.exit(main())
