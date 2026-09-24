#!/bin/sh

target="Debug"
targetPath="BuildBeacon/bin/$target/net48"
targetAssembly="BuildBeacon.dll"
valheimPath=""
bepinexPath=""
deployPath=""
projectPath="./BuildBeacon"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --target)
        target="$2"; shift 2 ;;
    --target-path)
        targetPath="$2"; shift 2 ;;
    --target-assembly)
        targetAssembly="$2"; shift 2 ;;
    --valheim-path)
        valheimPath="$2"; shift 2 ;;
    --bepinex-path)
        bepinexPath="$2"; shift 2 ;;
    --deploy-path)
        deployPath="$2"; shift 2 ;;
    --project-path)
        projectPath="$2"; shift 2 ;;
    *)
        echo "Warning: Unknown argument $1" >&2; shift ;;
  esac
done

# order of precedence: MOD_DEPLOYPATH > BEPINEX_PATH > VALHEIM_INSTALL > default path
if [ -z "$deployPath" ]; then
    if [ -z "$bepinexPath" ]; then
        if [ -z "$valheimPath" ]; then
            deployPath="$HOME/.local/share/Steam/steamapps/common/Valheim/BepInEx/plugins"
        else
            deployPath="$valheimPath/BepInEx/plugins"
        fi
    else
        deployPath="$bepinexPath/plugins"
    fi
fi

# strip .dll extension
name=$(echo "$targetAssembly" | sed 's/\.dll//')

if [ "$target" = "Debug" ]; then
    plug="$deployPath/$name"
    echo "Copying $targetAssembly to $plug"

    mkdir -p "$plug"
    cp "$targetPath/$targetAssembly" "$plug"
    # copy if it exists
    [ -e "$targetPath/$name.pdb" ] && cp "$targetPath/$name.pdb" "$plug"
fi

if [ "$target" = "Release" ]; then
    # The Thunderstore zip is built, checked and published by tools/release/release.py (docs/releasing.md).
    echo "Release build done. Package it with: uv run --no-project python tools/release/release.py package"
fi
