---
name: hydrate
description: Load the BuildBeacon project's working context at the start of a session or right after a context clear, compaction or reset - read the newest dated handoff in docs/handoff/, the running notes in current.md and docs/open-items.md, and compare them with the git state, then summarise where things stand. Use this whenever a session starts fresh on this repo, after /clear or /compact, or when the user says hydrate, catch up, load context, "read the handoff", "where were we", or "what's next". Pair with the handoff skill, which writes what this reads.
---

# Hydrate after a context clear

A fresh session knows only CLAUDE.md. The project's working memory is in `docs/handoff/`: the newest dated handoff
(`YY_M_DD<letter>.md`) is the complete picture as of its base commit, `current.md` is what changed after it, and
`docs/open-items.md` is the to-do list. Hydrating means reading those, checking them against the repository, and
telling the user where things stand, before touching anything.

## Steps

1. **Orient:**
   ```bash
   bash .claude/skills/hydrate/scripts/hydrate.sh
   ```
   It prints the newest handoff's name and base commit, the branch, uncommitted files, the commits made since the
   base, current.md and the open items' headings.

2. **Read in full,** in this order, with the Read tool rather than skimming:
   1. the newest dated handoff it named (it replaces all older ones; do not read those unless it points to them),
   2. `docs/handoff/current.md`,
   3. `docs/open-items.md`.
   Skill files load on their own when a task needs them; do not read them all up front.

3. **Reconcile with git.** Commits since the base commit that current.md does not mention are work the notes missed:
   read their messages (`git log <base>..HEAD`) and, if needed, their diffs. Uncommitted files are work in progress
   from the last session: look at `git diff --stat` and name them to the user rather than assuming they are finished.

4. **Check the environment only as far as the next task needs.** Examples: `dotnet --list-sdks` before using
   `tools/sigdump` (needs SDK 10); the Unity or Blender MCP tools before Unity or Blender work; the log-tail script
   when the user reports something from the game. Do not rebuild, deploy or commit as part of hydrating.

5. **Report,** briefly:
   - the branch, the handoff it came from, and anything done since it (commits, uncommitted work),
   - what the project is at in one or two sentences,
   - the open items, the in-game checks awaiting the user marked as such,
   - anything that looks inconsistent (notes vs git, a missing file, an environment problem),
   - then ask what to work on, or continue with the task the user already gave.

While working afterwards, append to current.md as things change, so the next handoff has them.
