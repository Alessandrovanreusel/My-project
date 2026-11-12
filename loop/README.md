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

The cross-review only counts if the reviewer **actually saw the code**. The loop checks this from
its transcript: a file counts as seen when its tools showed it most of the file, or nearly every
changed line of it (a diff review). Verification rigs are left out of the count. Below 80% of the
files, it is asked again with the unread files named; if it is still shallow, its review is kept
but marked *partial* in the report. (The first real cross-review answered "0 findings" after 16
seconds, having seen 5 of 10 files.)

| Situation | What the loop does |
|---|---|
| A skill or story asks you to decide | Decides from evidence, writes it in the queue, carries on |
| An AC needs your eyes or ears | Gathers the evidence, queues it, lets later stories build on it |
| An AC says you must approve something before a later story starts | Never starts that story; does other work, pings your phone once, and starts it the moment you approve |
| Compile errors, failing tests, review findings | Fixes them itself |
| A phase fails | Retries in a fresh session with a hint. Parks the unit after 3 failures that committed nothing, or 6 of any kind (a session that runs out of context on a big story commits its work and hands over; that is progress, not a failure) |
| The final check (close) changes the game itself to make a story pass | Runs one more fresh close to verify that change before merging: a session never certifies its own build. Rigs and docs don't count |
| A real blocker (login, purchase, licence, missing asset) | Parks the unit, pings your phone, moves to work that doesn't depend on it |
| Unity unreachable | Restarts Unity; after 2 failed restarts in a row it stops everything and pings you |
| Claude usage limit | Waits until the reset time the limit names (or 20 minutes if it names none), then starts a fresh session that is told it was cut off and checks git first. Pings you only for waits over 3 hours |
| Your weekly Claude allowance is 90% used | Pauses before its next phase and pings you once, leaving you the rest; resumes when the week resets. To change the share, put `"weekly_budget": 0.95` (or `1.0` for all of it) in `loop/runtime/config.local.json`. It takes effect within a minute, even mid-pause |
| The laptop sleeps (lid closed, on battery) | Nothing runs while Windows is asleep: the session freezes and carries on when it wakes. Asleep time does not count against a session's time limit. **To let it work while you're away, leave the lid open and the charger in** (or set "When I close the lid: Do nothing" for "Plugged in") |
| A fix to the loop itself lands on master while it runs | The working tree gets it when the loop next starts a unit or resumes a branch (it brings master in then); the loop notices its own code changed and restarts itself on the new code, after saving its state. To apply a fix sooner, stop and start the loop: that resume brings it in |
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
