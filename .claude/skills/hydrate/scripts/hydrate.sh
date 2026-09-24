#!/usr/bin/env bash
# Print what a fresh session needs first: the newest dated handoff and its base commit, git state and the commits
# made since that base, the running notes (current.md) and the open action items. Read the handoff file itself in full
# afterwards; this only orients.
# Usage: bash .claude/skills/hydrate/scripts/hydrate.sh
set -u
root="$(git rev-parse --show-toplevel)"
cd "$root"

# Newest handoff by its date and letter, not by file time: YY_M_DD + letter, e.g. 26_9_23B.md.
latest="$(ls docs/handoff/ 2>/dev/null | grep -E '^[0-9]+_[0-9]+_[0-9]+[A-Z]\.md$' \
  | awk -F'[_.]' '{ d=$3; l=substr(d, length(d)); d=substr(d, 1, length(d)-1); printf "%02d%02d%02d%s %s\n", $1, $2, d, l, $0 }' \
  | sort | tail -1 | cut -d' ' -f2)"
if [ -z "$latest" ]; then
  echo "!! no dated handoff in docs/handoff/"
  exit 1
fi
base="$(grep -o -m1 -E 'base commit `[0-9a-f]{7,40}`' "docs/handoff/$latest" | grep -o -E '[0-9a-f]{7,40}')"

echo "=== newest handoff: docs/handoff/$latest (base commit ${base:-unknown})"
sed -n '3,6p' "docs/handoff/$latest"
echo
echo "=== git: $(git branch --show-current)  HEAD $(git log -1 --format='%h %s')"
git status --short | head -30
if [ -n "$base" ] && git cat-file -e "$base^{commit}" 2>/dev/null; then
  n="$(git rev-list --count "$base"..HEAD)"
  echo "--- $n commit(s) since the handoff base:"
  git log --oneline "$base"..HEAD | head -30
fi
echo
echo "=== docs/handoff/current.md (changes since the handoff)"
cat docs/handoff/current.md 2>/dev/null || echo "!! missing"
echo
echo "=== docs/open-items.md (numbered items)"
grep -E '^(## |[0-9]+\. )' docs/open-items.md 2>/dev/null | cut -c1-150 || echo "!! missing"
