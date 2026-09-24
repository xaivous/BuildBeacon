#!/usr/bin/env bash
# Print the relevant lines from the current game session in the Dev profile's BepInEx log.
# Usage: bash .claude/skills/valheim-debug/scripts/log-tail.sh [extra-grep-pattern]
set -u
log="$APPDATA/r2modmanPlus-local/Valheim/profiles/Dev/BepInEx/LogOutput.log"
extra="${1:-}"

if [ ! -f "$log" ]; then
  echo "!! log not found: $log"
  exit 1
fi

echo "log written $(date -r "$log" '+%m-%d %H:%M'), $(wc -l < "$log") lines"
start=$(grep -n 'Loading \[BuildBeacon' "$log" | tail -1 | cut -d: -f1)
if [ -z "$start" ]; then
  echo "!! no 'Loading [BuildBeacon' line: the mod did not load in this session"
  exit 1
fi
echo "session starts at line $start"
echo

pattern='BuildBeacon|HarmonyX|Error|Exception|Warning.*(Jotunn|Unity Log)|Placed xai_build_beacon|placed at|Failed to|not found|Falling back|Cannot instantiate'
[ -n "$extra" ] && pattern="$pattern|$extra"

tail -n +"$start" "$log" \
  | grep -E "$pattern" \
  | grep -v -E 'Ambiguous asset name|Missing audio clip|Unloading unused assets' \
  | cut -c1-260
