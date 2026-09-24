#!/usr/bin/env bash
# Copy the freshly built asset bundle from the Unity project into the mod, then build and verify the plugin.
# Usage: bash .claude/skills/unity-bundle/scripts/copy-bundle-and-build.sh
set -u
cd "$(dirname "$0")/../../../.." || exit 1

src="BuildBeaconUnity/Assets/Output/buildbeacon"
dst="BuildBeacon/Assets/buildbeacon"

if [ ! -f "$src" ]; then
  echo "!! no bundle at $src - rebuild it in Unity first (see references/rebuild-bundle.cs)"
  exit 1
fi

echo "bundle: $(stat -c %s "$src") bytes, built $(date -r "$src" +%H:%M:%S)"
cp "$src" "$dst" || exit 1
echo "copied to $dst"

bash .claude/skills/build-deploy/scripts/build-and-verify.sh
