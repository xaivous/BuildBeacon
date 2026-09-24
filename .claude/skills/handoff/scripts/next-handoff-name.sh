#!/usr/bin/env bash
# Print the path of the next dated handoff file for today: docs/handoff/YY_M_DD plus the first free letter (A, B, ...).
# Usage: bash .claude/skills/handoff/scripts/next-handoff-name.sh
set -eu
root="$(git rev-parse --show-toplevel)"
dir="$root/docs/handoff"
prefix="$(date +%y)_$(date +%-m)_$(date +%-d)"
for letter in A B C D E F G H I J K L M N O P Q R S T U V W X Y Z; do
  if [ ! -e "$dir/${prefix}${letter}.md" ]; then
    echo "docs/handoff/${prefix}${letter}.md"
    exit 0
  fi
done
echo "!! 26 handoffs already today in $dir" >&2
exit 1
