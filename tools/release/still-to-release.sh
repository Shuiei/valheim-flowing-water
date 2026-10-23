#!/usr/bin/env bash
# Whether this commit's version is still to be released on GitHub: CircleCI's github-release job runs it before it
# tags anything (.circleci/continue.yml). No tag v<VERSION> yet: it exits 0, and the job makes the release. The tag on
# this commit with its release and zip on GitHub (a rerun, after a publish job failed): the job's steps stop here
# (circleci-agent step halt), and the release goes on to the mod sites. The tag on another commit, or a release
# without its zip, fails: for the maintainer to look at. Tested by tests/FlowingWater.Tests (StillToReleaseTests).
# Usage, from the repository's root: tools/release/still-to-release.sh <dist>   (dist: where the package job left
# FlowingWater-<VERSION>.zip; CircleCI gives CIRCLE_SHA1, CIRCLE_PROJECT_USERNAME, CIRCLE_PROJECT_REPONAME and the
# mod-token context GITHUB_TOKEN)
set -euo pipefail
dist=$1
git fetch -q --tags origin
version=$(tr -d '[:space:]' < VERSION)
ls "$dist/FlowingWater-$version.zip"
if ! git rev-parse -q --verify "refs/tags/v$version" > /dev/null; then
  exit 0
fi

tagged=$(git rev-parse "v$version^{commit}")
if [ "$tagged" != "$CIRCLE_SHA1" ]; then
  echo "v$version is tagged already, on $tagged, not on $CIRCLE_SHA1 (released from another pipeline?)."; exit 1
fi
assets=$(GH_TOKEN=$GITHUB_TOKEN gh release view "v$version" --repo "$CIRCLE_PROJECT_USERNAME/$CIRCLE_PROJECT_REPONAME" --json assets --jq '.assets[].name') \
  || { echo "v$version is tagged on this commit but GitHub has no release for it: make it by hand."; exit 1; }
if ! printf '%s\n' "$assets" | grep -qxF "FlowingWater-$version.zip"; then
  echo "The GitHub release v$version has no FlowingWater-$version.zip: add it by hand."; exit 1
fi
echo "v$version is released on GitHub already, from this commit: nothing more to do here."
circleci-agent step halt
