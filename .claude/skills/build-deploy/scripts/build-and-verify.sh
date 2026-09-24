#!/usr/bin/env bash
# Build the mod in Debug and verify the deploy actually landed in the r2modman Dev profile.
# Usage: bash .claude/skills/build-deploy/scripts/build-and-verify.sh [Debug|Release]
set -u
cd "$(dirname "$0")/../../../.." || exit 1

config="${1:-Debug}"
built="BuildBeacon/bin/$config/net48/BuildBeacon.dll"
deployed="$APPDATA/r2modmanPlus-local/Valheim/profiles/Dev/BepInEx/plugins/BuildBeacon/BuildBeacon.dll"

echo "=== dotnet build ($config)"
dotnet build BuildBeacon.sln -c "$config" -nologo -v:m 2>&1 | grep -E ' error |warning CS|Build succeeded|Build FAILED|Error\(s\)' | sort -u | cut -c1-300

if [ ! -f "$built" ]; then
  echo "!! built DLL not found at $built"
  exit 1
fi

echo "=== artifacts"
built_size=$(stat -c %s "$built")
built_time=$(date -r "$built" +%H:%M:%S)
echo "built:    $built_size bytes at $built_time"

if [ "$config" = "Debug" ]; then
  if [ -f "$deployed" ]; then
    dep_time=$(date -r "$deployed" +%H:%M:%S)
    if [ "$deployed" -ot "$built" ]; then
      echo "deployed: STALE ($dep_time) - the copy into the Dev profile did not happen"
    else
      echo "deployed: ok ($dep_time)"
    fi
  else
    echo "deployed: MISSING - $deployed"
  fi
fi

if tasklist 2>/dev/null | grep -qi 'valheim.exe'; then
  echo "valheim:  RUNNING - it holds the deployed DLL open; close it and rebuild to deploy"
else
  echo "valheim:  not running"
fi

echo "=== embedded resources"
resources=$(powershell -NoProfile -Command "[System.Reflection.Assembly]::LoadFile('$(cygpath -w "$PWD/$built")').GetManifestResourceNames() -join ','" 2>/dev/null)
if echo "$resources" | grep -q 'buildbeacon'; then
  echo "bundle:   embedded ($resources)"
else
  echo "bundle:   MISSING - check the EmbeddedResource entry in BuildBeacon/BuildBeacon.csproj"
fi
