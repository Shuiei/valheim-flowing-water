#!/usr/bin/env bash
# The package in a real Valheim dedicated server: what the build cannot check, since it needs the game.
# Installs into <server> (a dedicated server, Steam app 896660) BepInExPack_Valheim (the version
# thunderstore/manifest.json depends on, from Thunderstore) and the package's plugin as a mod manager
# would, with a config that gives the world fixed sources. Then two runs of the server on the same new
# world (its seed is random), read from BepInEx's log:
#   1. FlowingWater loads, starts its simulation when the world opens, finds the fixed sources, and the
#      water of one on dry ground stays in its zone (the depth maps DebugSeconds logs, 20 s in); no
#      error, warning or exception of its own
#   2. after a restart, the water saved with the world (its bricks) is loaded again
# Usage: tools/server-smoke.sh <server> <package.zip>   (needs curl and unzip; changes <server>)
# The logs of both runs are left in <server>/smoke-logs/ (CI keeps them as artifacts).
set -euo pipefail
server=$(realpath "${1:?dedicated server folder}")
zip=$(realpath "${2:?package zip}")
repo=$(cd "$(dirname "$0")/.." && pwd)
[ -x "$server/valheim_server.x86_64" ] || { echo "$server is not a Valheim dedicated server" >&2; exit 1; }
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
logs="$server/smoke-logs"
rm -rf "$logs"; mkdir -p "$logs"
version=$(unzip -p "$zip" manifest.json | sed -n 's/.*"version_number": *"\([^"]*\)".*/\1/p')
name="FlowingWater"

# ---- Install.
pack=$(grep -o '"denikson-BepInExPack_Valheim-[0-9.]*"' "$repo/thunderstore/manifest.json" | tr -d '"' | sed 's/.*-//')
curl -fsSL -o "$work/pack.zip" "https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/$pack/"
unzip -qo "$work/pack.zip" 'BepInExPack_Valheim/*' -d "$work/pack"
cp -a "$work/pack/BepInExPack_Valheim/." "$server/"
plugins="$server/BepInEx/plugins" config="$server/BepInEx/config"
rm -rf "$plugins"
mkdir -p "$plugins/Tie-FlowingWater" "$config"
unzip -qjo "$zip" 'plugins/*' -d "$plugins/Tie-FlowingWater"
# Nine sources up to 570 m from the world's centre: in a random world some are under the sea (which
# takes their water at once), and some on land. The depth maps logged every 20 s.
cat > "$config/Tie.FlowingWater.cfg" <<'EOF'
[Server]
FixedSources = 0,0,5; 200,0,5; -200,0,5; 0,200,5; 0,-200,5; 400,400,5; -400,400,5; 400,-400,5; -400,-400,5
DebugSeconds = 20
EOF

# ---- Run.
# Starts the server as BepInExPack's start_server_bepinex.sh does and stops it (it saves the world as it
# stops) once the log has a line matching $2 (or after $3 seconds); its BepInEx log is kept as $logs/$1.log.
run() {
  local run=$1 until=$2 limit=$3 log="$server/BepInEx/LogOutput.log" pid elapsed=0
  rm -f "$log"
  (
    cd "$server"
    export DOORSTOP_ENABLED=1 DOORSTOP_TARGET_ASSEMBLY=./BepInEx/core/BepInEx.Preloader.dll
    export LD_LIBRARY_PATH="./doorstop_libs:./linux64:${LD_LIBRARY_PATH:-}" LD_PRELOAD="libdoorstop_x64.so:${LD_PRELOAD:-}"
    export SteamAppId=892970
    exec ./valheim_server.x86_64 -nographics -batchmode -name "FlowingWater smoke test" -port 2456 \
      -world Smoke -password smoke-test-1 -public 0 -savedir "$work/saves"
  ) > "$logs/$run.console.log" 2>&1 &
  pid=$!
  until grep -qE "$until" "$log" 2>/dev/null; do
    if ! kill -0 "$pid" 2>/dev/null; then echo "The server stopped before: $until" >&2; break; fi
    if [ "$elapsed" -ge "$limit" ]; then echo "No '$until' after ${limit}s." >&2; break; fi
    sleep 2; elapsed=$((elapsed + 2))
  done
  kill -INT "$pid" 2>/dev/null || true
  for _ in $(seq 60); do kill -0 "$pid" 2>/dev/null || break; sleep 1; done
  kill -KILL "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  cp "$log" "$logs/$run.log" 2>/dev/null || touch "$logs/$run.log"
  echo "Run '$run': stopped after ${elapsed}s; its log is $logs/$run.log"
}

failed=0
expect() {   # $1 run, $2 what, then a grep -E pattern that must (or with !, must not) be in the run's log
  local run=$1 what=$2 wanted=yes found=no
  shift 2
  if [ "$1" = '!' ]; then wanted=no; shift; fi
  if grep -qaE "$1" "$logs/$run.log"; then found=yes; fi
  if [ "$found" = "$wanted" ]; then
    echo "  ok: $what"
  else
    echo "  FAILED: $what"; failed=1
    if [ "$wanted" = no ]; then grep -aE -m 20 "$1" "$logs/$run.log" | sed 's/^/    /'; fi
  fi
}
escaped=$(printf '%s' "$version" | sed 's/[.]/\\&/g')

# No error or warning from the mod in a run's log, but for the water slowing down to its StepBudget: a build
# machine is slower than a game server, and that warning is the budget at work (noted, not failed).
slowed="slows down to keep the server's frames short"
quiet() {   # $1 run
  local found
  found=$(grep -aE "^\[(Error|Fatal|Warning) *: *$name\]" "$logs/$1.log" | grep -avF "$slowed" || true)
  if [ -z "$found" ]; then
    echo "  ok: no error or warning from $name"
  else
    echo "  FAILED: no error or warning from $name"; failed=1
    printf '%s\n' "$found" | head -20 | sed 's/^/    /'
  fi
  if grep -qaF "$slowed" "$logs/$1.log"; then echo "  note: the water slowed down to its StepBudget"; fi
}

# A zone's depth map with water in it: "zone <x>,<z>: <volume> m³, ground ...".
wet='^zone -?[0-9]+,-?[0-9]+: [1-9][0-9]*\.[0-9] m³'
run water "$wet" 900
echo "Run 'water': a new world with fixed sources"
expect water "$name $version loaded" "Loading \[$name $escaped\]"
expect water "the simulation started with the world" "World open: water simulation started"
expect water "the fixed sources found" "Water: 9 sources and drains"
expect water "the water made bricks" "Water: [1-9][0-9]* bricks of"
expect water "a zone's depth map, with water in it" "$wet"
quiet water
expect water "no exception in $name" ! "at FlowingWater\."

run reload 'Water: [0-9]+ bricks of [0-9.]+ m voxels, [0-9.]+ m³ read' 600
echo "Run 'reload': the same world again"
expect reload "the saved water loaded" "Water: [1-9][0-9]* bricks of [0-9.]+ m voxels, [1-9][0-9]*\.[0-9] m³ read from bricks"
quiet reload
expect reload "no exception in $name" ! "at FlowingWater\."

if [ "$failed" != 0 ]; then
  echo "Smoke test FAILED (logs in $logs)." >&2
  exit 1
fi
echo "Smoke test passed."
