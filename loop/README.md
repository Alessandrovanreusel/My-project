# The unattended loop

The loop builds the backlog on its own, story after story, through every epic, and only contacts you
when something genuinely needs you. Decisions it takes for you and checks that need your eyes are
saved up for you to review in one sitting.

## Start, stop, check

From the project folder, in PowerShell:

| What | Command |
|---|---|
| Start (runs until done or blocked) | `powershell -ExecutionPolicy Bypass -File loop\start.ps1` |
| Run one story only | `powershell -ExecutionPolicy Bypass -File loop\start.ps1 -Only 1-15-record-a-clip` |
| Stop after the current phase | `powershell -File loop\stop.ps1` |
| Stop immediately | `powershell -File loop\stop.ps1 -Now` |
| What will it do next, and why? | `python loop\loop.py --dry-run` |
| Check the plumbing (Unity, guard, tools) | `python loop\loop.py --smoke` |

When you come back, read these two files:

- **`loop/runtime/human-queue.md`**: approvals that are holding work back (section 0, the most
  urgent), blockers, the decisions it took for you (tick or veto each), and the checks that need your
  eyes, each with the evidence to look at.
- **`loop/runtime/reports/<date>.md`**: one line per phase. What ran, how long it took, the outcome.

Everything in `loop/runtime/` stays on this PC. It is git-ignored, because this repository is public.

## Before you start it

1. **Unity**: it starts the editor itself if it isn't running. Close any open dialog first.
   Recommended: *Edit → Preferences → General → Interaction Mode → No Throttling*, so the editor
   doesn't slow down in the background.
2. **OneDrive**: pause syncing while the loop runs (right-click the cloud icon → Pause syncing). The
   project lives in OneDrive, and file locks during builds and imports can break a run.
3. **Don't use the PC at the same time.** The loop drives Unity and opens its own Chrome tabs. It
   keeps Windows awake by itself while it runs.
4. **Phone**: install **ntfy** and subscribe to the topic in `loop/runtime/config.local.json`
   (`ntfy_topic`). Treat that name like a password: anyone who knows it can read the messages.

## How it works

```
pick the next unit  (sprint-status.yaml + dependencies.yaml, skipping anything parked)
  └─ new branch loop/<unit>, tag loop/pre-<unit> on master
       ├─ create   gds-create-story                         fresh Claude session
       ├─ dev      gds-dev-story + rig + captures + Gemini   fresh Claude session
       ├─ review   gds-code-review, runs scenarios           fresh Claude session
       ├─ cross    read-only Claude session, Sonnet 5.5      different model, fresh eyes
       ├─ fix      reproduce each cross-review finding, fix the real ones
       └─ close    tests, console, evidence, story file      fresh Claude session
  └─ merge to master, push  (or park it and move on)
```

Each phase is a **new** `claude -p` session that re-reads `CLAUDE.md`, the project memory,
`loop/lessons.md` and the phase brief in `loop/prompts/`. It ends by writing a small result file,
which is the only thing the orchestrator trusts. When an epic has no stories yet, a **plan-epic**
unit breaks it down; when every story in an epic has merged, a **retro** unit writes its lessons.

| Situation | What the loop does |
|---|---|
| A skill or story asks you to decide | Decides from evidence, writes it in the queue, carries on |
| An AC needs your eyes or ears | Gathers the evidence, queues it, lets later stories build on it |
| An AC says you must approve something before a later story starts | Never starts that story; does other work, pings your phone once, and starts it the moment you approve |
| Compile errors, failing tests, review findings | Fixes them itself |
| A phase fails | Retries in a fresh session with a hint, up to 3 times, then parks the unit |
| A real blocker (login, purchase, licence, missing asset) | Parks the unit, pings your phone, moves to work that doesn't depend on it |
| Unity unreachable | Restarts Unity; after 2 failed restarts in a row it stops everything and pings you |
| Claude usage limit | Waits 20 minutes and retries, for up to 30 hours |
| Nothing left that it can do | Stops and pings you |

**Approve something the loop is waiting for** (section 0 of the queue, e.g. ADR-4): tell Claude
"approve ADR-4", or add the line `APPROVED ADR-4` to `loop/runtime/approvals.txt` yourself. You
don't need to stop or restart anything. If only gated work is left, the loop waits and lets the PC
sleep; it carries on when the PC is awake again. The gates themselves are listed in
`loop/dependencies.yaml` under `gates:`.
**Resume a parked unit:** fix the cause, then tell Claude "resume `<unit>` in the loop". It clears
the `parked` entry in `loop/runtime/state.json`, and the next start picks the unit up on its branch.
**Undo a unit:** every unit starts from the tag `loop/pre-<unit>`.

## Safety

You chose to run sessions with **no permission prompts** (`--dangerously-skip-permissions`). Hooks
still run in that mode, so `.claude/hooks/loop_guard.py` is a seatbelt. It is active only in loop
sessions (`CAMERA_LOOP=1`) and refuses:

- **git:** force-pushes and pushes, history rewrites, branch switching, `git add -A`;
- **files:** deletes outside the project, and edits to the guard and settings files themselves;
- **outward-facing actions:** e-mail and other account actions, publishing, uploading files, and
  checkout pages in Chrome.

Test it with `python loop\tests\test_loop_guard.py`. It is a seatbelt, not a jail: a few clear
rules, with everything else allowed on purpose.
