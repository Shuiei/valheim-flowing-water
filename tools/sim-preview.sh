#!/usr/bin/env bash
# Runs the water moved on the graphics card (src/Client/SimGrid.cs, FlowingWaterSim.compute) on the preview's scenes in
# the Unity editor, with a display, and writes what it measured and frames to the folder given (default: preview).
#   tools/sim-preview.sh /some/folder
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$(realpath -m "${1:-preview}")"
unity="${UNITY:-$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity}"
mkdir -p "$root/unity/Assets/Editor/Shared" "$out"
# Unity compiles C# 9: the file-scoped namespace becomes a block.
for file in SimGrid SprayParticles; do
  sed -e 's/^namespace \(.*\);$/namespace \1 {/' "$root/src/Client/$file.cs" > "$root/unity/Assets/Editor/Shared/$file.cs"
  echo "}" >> "$root/unity/Assets/Editor/Shared/$file.cs"
done
FW_PREVIEW_OUT="$out" xvfb-run -a -s "-screen 0 1920x1080x24" "$unity" -batchmode -quit -projectPath "$root/unity" \
  -executeMethod SimPreview.Run -logFile "$out/unity.log"
