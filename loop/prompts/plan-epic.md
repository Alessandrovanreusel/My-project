## Your task — phase PLAN-EPIC for epic {epic} (attempt {attempt})

Epic {epic} has no stories yet. Break it into stories, just-in-time, so the loop can build it.

1. Read the epic in `_bmad-output/planning-artifacts/epics.md`: its entry in the Epic List, its FRs,
   and its line under **Known seeds**. Read the GDD sections it serves, the architecture's epic
   mapping, `loop/lessons.md`, and what the finished epics actually built (their story files).
2. Use the **`gds-create-epics-and-stories`** approach to write the stories for **this epic only**.
   Add them to `epics.md` as a new `## Epic {epic}: …` section, in the same format as Epic 1:
   `### Story {epic}.N: Title`, user story, then Given/When/Then acceptance criteria that observe
   the outermost surface the player sees. The first story of an epic with a known blocker (for
   example Epic 2's town-map FBX export, AR9) comes first.
3. Size stories like Epic 1's: one verifiable player-facing capability each, 3–8 tasks' worth.
4. Add every new story key to `_bmad-output/implementation-artifacts/sprint-status.yaml` under this
   epic's block as `backlog`, in order (use the `{epic}-N-kebab-title` key format), and set
   `epic-{epic}: in-progress`. **Never put `: ` or a trailing `:` inside `last_updated`** (it breaks
   the YAML; see project memory). Verify the file still parses with `yaml.safe_load`.
5. Add each new story's **real** dependencies to `story_depends_on` in `loop/dependencies.yaml`:
   only "cannot be built until that exists". Stories in other epics may be named. Keep the file
   parseable.
6. Design questions the epic leaves open follow the decide-and-flag rule; scope-level ones
   (cutting or adding a feature the GDD names) go in `decisions` with care.

{previous_failure}

**Done when:** the epic's stories are in `epics.md`, in sprint-status as `backlog`, their
dependencies are in `loop/dependencies.yaml`, all three files parse, and everything is committed on
`{branch}`.
