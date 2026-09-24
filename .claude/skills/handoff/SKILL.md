---
name: handoff
description: Close out a working session on the BuildBeacon mod before the context is cleared or compacted - fold docs/handoff/current.md into a new dated handoff file (YY_M_DD plus a letter), reset current.md, tidy docs/open-items.md and point everything at the new file, so the next session can pick up cleanly. Use this whenever the user says handoff, hand off, wrap up, "before I clear", "I'm going to /clear", "save context", "end of session", "close out current.md", or asks for a new timestamped handoff doc. Pair with the hydrate skill, which reads the result after the clear.
---

# Handoff before a context clear

The project keeps its memory in three files, described in CLAUDE.md:

- `docs/handoff/YY_M_DD<letter>.md`: dated handoffs. The newest one is a complete, standalone picture of the project:
  what it is, a map, where each kind of truth lives, the loops, the gotchas, the decisions, pointers and side notes.
- `docs/handoff/current.md`: running notes since the newest handoff, appended as work happens.
- `docs/open-items.md`: the only to-do list.

A handoff turns "newest dated file + current.md" into a new dated file that stands on its own, then empties
current.md. The next session reads only the new file (plus an empty current.md), so anything not written into it is
lost. Write it for a reader with no memory of this session.

## Steps

1. **Check the working tree.** Run `git status --short` and `git log --oneline -5`. If there is uncommitted work,
   tell the user and ask whether to commit it first. Do not commit on your own: the project rule is commits only when
   the user says "commit". Note the HEAD commit: it becomes the new handoff's base commit.

2. **Bring the notes up to date.** Before folding, make sure current.md covers everything this session changed,
   decided or learned (check the session and `git log` since the last handoff's base commit). Make sure open-items.md
   has every follow-up the user asked for, with verified items moved to Done. Add anything missing now.

3. **Pick the file name:**
   ```bash
   bash .claude/skills/handoff/scripts/next-handoff-name.sh
   ```
   It prints `docs/handoff/YY_M_DD<letter>.md` for today, with the first free letter.

4. **Write the new handoff.** Read the previous dated handoff and current.md in full. Write the new file with the same
   sections as the previous one, rewritten rather than appended:
   - Header: its own name as the title, the date and time, the branch, `handoff base commit \`<sha>\`` (the hydrate
     script reads this exact phrase), and which file it replaces.
   - What this is, as the project stands now (not as it stood at the last handoff).
   - Map: add new files and folders, drop removed ones, update one-line descriptions that changed.
   - Where the truth lives: add any new source of truth (config keys, tables, prefabs, scripts).
   - Loops: new or changed pipelines, with the exact commands.
   - Healthy log signature: the lines a good startup now prints.
   - Gotchas: keep the old ones that can still bite, add the new ones from current.md, merge duplicates.
   - Decisions: fold in current.md's decisions; replace ones that were overturned instead of listing both.
   - Open items: a pointer to docs/open-items.md and a one-line summary of what is open.
   - Side notes: environment, remote, user preferences learned.
   Keep it dense and factual. It replaces the previous file as the starting point, so it must not depend on it.

5. **Reset current.md** to the template, pointing at the new file and its base commit:
   ```markdown
   # BuildBeacon handoff: current

   Notes since the last handoff, `<new file>` (base commit `<sha>`). Read that file first; this one is the delta.
   Append here as work happens: what changed, decisions, new gotchas. Action items live in `docs/open-items.md`. At the
   next handoff (the `handoff` skill), fold this into a new dated file and reset it to this template.

   ## Changed since <new file stem>

   Nothing yet.

   ## Decisions

   None yet.

   ## New gotchas

   None yet.
   ```

6. **Tidy open-items.md.** Clear the Done list, since the handoff and git history now hold it, and leave a line saying
   which handoff cleared it. Renumber the open items if anything moved.

7. **Check the pointers.** CLAUDE.md describes the convention, not a specific file, so it normally needs no change.
   Search for references to the previous handoff by name (`grep -rn "<old stem>" docs CLAUDE.md .claude`) and update
   any that should now name the new one. Older dated files stay as history; do not edit or delete them.

8. **Verify, then report.** Run the hydrate script to see what the next session will see:
   ```bash
   bash .claude/skills/hydrate/scripts/hydrate.sh
   ```
   It must find the new file and its base commit, show 0 commits since the base (or only the handoff commit), and
   show an empty current.md. Then tell the user the new file's name, what it folded in, and that nothing is committed
   until they say so. When they do, commit the new file, current.md, open-items.md and any pointer changes together as
   one handoff commit.
