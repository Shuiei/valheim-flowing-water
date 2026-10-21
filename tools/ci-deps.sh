#!/usr/bin/env bash
# Fetch BepInEx's core (BepInEx.dll, 0Harmony.dll, ...) into <dir>/BepInEx/core, from Thunderstore (the
# BepInExPack_Valheim version thunderstore/manifest.json depends on): with the game's DLLs in
# <dir>/valheim_server_Data/Managed (CircleCI's game-dlls job), <dir> is a ValheimDir the plugin builds
# against. Usage: tools/ci-deps.sh <dir>   (needs curl and unzip)
set -euo pipefail
dir=$(realpath -m "${1:?output folder}")
repo=$(cd "$(dirname "$0")/.." && pwd)
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
id=$(grep -o '"denikson-BepInExPack_Valheim-[0-9.]*"' "$repo/thunderstore/manifest.json" | tr -d '"')
[ -n "$id" ] || { echo "denikson-BepInExPack_Valheim is not a dependency in thunderstore/manifest.json" >&2; exit 1; }
curl -fsSL -o "$work/pack.zip" "https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/${id##*-}/"
mkdir -p "$dir/BepInEx/core"
unzip -joq "$work/pack.zip" 'BepInExPack_Valheim/BepInEx/core/*' -d "$dir/BepInEx/core"
echo "$id: $dir/BepInEx/core"
