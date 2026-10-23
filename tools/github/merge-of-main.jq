# A pull request's new head, as GitHub's commits API gives it (repos/<repo>/commits/<sha>), checked to be GitHub's
# own merge of main into the old head, as update-branch makes it: committed by web-flow and signed by GitHub (its
# signature verified), with the old head ($old) as first parent and one commit more, nothing else. Prints that
# second parent, whose ancestry in main the workflow then checks; prints nothing for any other commit (a push by the
# author, which may hold any files whatever its parents). Used by .github/workflows/update-approved-prs.yml, tested by
# tests/FlowingWater.Tests/MergeOfMainTests.cs.
#   jq -r --arg old <old head sha> -f tools/github/merge-of-main.jq < commit.json
if .commit.verification.verified == true
  and .committer.login == "web-flow"
  and (.parents | length) == 2
  and .parents[0].sha == $old
then .parents[1].sha
else empty
end
