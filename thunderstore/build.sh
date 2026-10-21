#!/usr/bin/env bash
# Build the mod's package into <dist>: FlowingWater-<version>.zip with manifest.json, icon.png,
# README.md (thunderstore/README.md, the mod page), CHANGELOG.md and LICENSE at the root and the DLL in
# plugins/ (mod managers flatten any other folder).
# The version is VERSION's. The release (.circleci/continue.yml) puts this zip on GitHub, Thunderstore
# and Hexium (tools/release/publish.cs).
# Usage: thunderstore/build.sh <dist> [msbuild properties...]
#   e.g. thunderstore/build.sh dist -p:ValheimDir=/opt/valheim -p:BepInExDir=/opt/bepinex/BepInEx
# (needs the .NET SDK, zip, and the DLLs at the paths in FlowingWater.csproj or the ones given).
set -euo pipefail
dist=$(realpath -m "${1:?output folder}"); shift
here=$(cd "$(dirname "$0")" && pwd); repo=$(dirname "$here")
dotnet=${DOTNET:-dotnet}
version=$(tr -d '[:space:]' < "$repo/VERSION")
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "VERSION is not <major>.<minor>.<patch>: $version" >&2; exit 1; }
grep -q "^## v$version — " "$repo/CHANGELOG.md" || { echo "CHANGELOG.md has no '## v$version — <date or unreleased>' section" >&2; exit 1; }
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
# PluginsDir: nowhere, so the build copies the DLL into no BepInEx folder.
build=("$repo/FlowingWater.csproj" -c Release -warnaserror -p:DebugType=none -p:PluginsDir="$work/none" -o "$work/build" "$@")
"$dotnet" build "${build[@]}" > "$work/build.log" || { grep -E 'error|warning' "$work/build.log" >&2; exit 1; }
mkdir -p "$work/pkg/plugins" "$dist"
cp "$work/build/FlowingWater.dll" "$work/pkg/plugins/"
cp "$here/icon.png" "$here/README.md" "$repo/CHANGELOG.md" "$repo/LICENSE" "$work/pkg/"
sed "s/@VERSION@/$version/g" "$here/manifest.json" > "$work/pkg/manifest.json"
out="$dist/FlowingWater-$version.zip"
rm -f "$out"
(cd "$work/pkg" && zip -qrX "$out" .)
echo "$out"
