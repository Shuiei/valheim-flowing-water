#!/usr/bin/env bash
# Checks the formatting as .editorconfig says (dotnet format, verifying only), as the Valheim world
# editor's tools/format-check.sh does: the plugin's and its unit tests' whitespace and code style, and the
# in-game recorder's whitespace (a test tool, not held to the plugin's style rules: tools/Recorder/Directory.Build.props).
# dotnet format checks nothing, and still exits 0, for a project that is not restored ("Required
# references did not load"): each project is restored first, and that message fails the check.
# Usage: tools/format-check.sh   (the game's DLLs as the build finds them: ValheimDir, BepInExDir)
set -euo pipefail
cd "$(dirname "$0")/.."
log=$(mktemp); trap 'rm -f "$log"' EXIT
check() {   # $1 project, then the dotnet format checks
  local project=$1; shift
  echo "== $project"
  dotnet restore "$project" -v quiet
  for check in "$@"; do
    dotnet format "$check" "$project" --verify-no-changes --no-restore 2>&1 | tee "$log"
    if grep -q "Required references did not load" "$log"; then
      echo "dotnet format $check checked nothing in $project: its references did not load (restore failed?)." >&2
      exit 1
    fi
  done
}
check FlowingWater.csproj whitespace style
check tools/Recorder/Recorder.csproj whitespace
check tests/FlowingWater.Tests/FlowingWater.Tests.csproj whitespace style
echo "Formatting as .editorconfig says."
