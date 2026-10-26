#!/usr/bin/env bash
# Runs the water moved on the graphics card (src/Client/SimGrid.cs, FlowingWaterSim.compute) on the preview's scenes in
# the Unity editor, with a display, and writes what it measured and frames to the folder given (default: preview).
#   tools/sim-preview.sh /some/folder
# A second argument picks another preview: ParticlePreview.Run (the particle water, src/Client/ParticleWater.cs).
#   tools/sim-preview.sh /some/folder ParticlePreview.Run
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$(realpath -m "${1:-preview}")"
method="${2:-SimPreview.Run}"
unity="${UNITY:-$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity}"
mkdir -p "$root/unity/Assets/Editor/Shared" "$out"
# Unity compiles C# 9: the file-scoped namespace becomes a block.
for file in SimGrid SprayParticles ParticleWater ParticleSurface ParticleSolids VolumeWater; do
  sed -e 's/^namespace \(.*\);$/namespace \1 {/' "$root/src/Client/$file.cs" > "$root/unity/Assets/Editor/Shared/$file.cs"
  echo "}" >> "$root/unity/Assets/Editor/Shared/$file.cs"
done
FW_PREVIEW_OUT="$out" xvfb-run -a -s "-screen 0 1920x1080x24" "$unity" -batchmode -quit -projectPath "$root/unity" \
  -executeMethod "$method" -logFile "$out/unity.log"
