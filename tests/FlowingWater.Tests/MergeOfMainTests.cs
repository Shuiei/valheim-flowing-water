using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The check the approved pull requests' update makes before asking CodeRabbit to approve again
/// (tools/github/merge-of-main.jq, run with jq): only GitHub's own merge of main into the old head passes, not a
/// commit with the same parents that is unsigned, committed by someone else, or has a parent more; the workflow runs
/// that check, from main.
/// </summary>
public class MergeOfMainTests
{
    private const string Old = "1111111111111111111111111111111111111111";

    private const string Main = "2222222222222222222222222222222222222222";

    [Fact]
    public void GitHubsOwnMergeOfMainPassesWithMainsCommit()
    {
        Assert.Equal(Main, Check(Commit()));
    }

    [Theory]
    [InlineData("unsigned")]
    [InlineData("unverified")]
    [InlineData("the author's")]
    [InlineData("no committer")]
    [InlineData("other first parent")]
    [InlineData("one parent")]
    [InlineData("three parents")]
    public void AnyOtherHeadIsRefusedEvenWithTheRightParents(string how)
    {
        string commit = how switch
        {
            "unsigned" => Commit(verification: """{"verified": false, "reason": "unsigned"}"""),
            "unverified" => Commit(verification: """{"verified": "true", "reason": "valid"}"""),
            "the author's" => Commit(committer: """{"login": "someone"}"""),
            "no committer" => Commit(committer: "null"),
            "other first parent" => Commit(parents: $$"""[{"sha": "{{Main}}"}, {"sha": "{{Old}}"}]"""),
            "one parent" => Commit(parents: $$"""[{"sha": "{{Old}}"}]"""),
            _ => Commit(parents: $$"""[{"sha": "{{Old}}"}, {"sha": "{{Main}}"}, {"sha": "3333"}]"""),
        };

        Assert.Equal("", Check(commit));
    }

    [Fact]
    public void NothingFromTheApiIsRefused()
    {
        Assert.Equal("", Check(""));
    }

    [Fact]
    public void TheWorkflowRunsTheCheckFromMain()
    {
        string workflow = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", "update-approved-prs.yml"));

        Assert.Contains("jq -r --arg old \"$sha\" -f tools/github/merge-of-main.jq", workflow);
        Assert.Contains("branches: [main]", workflow);
        Assert.Contains("uses: actions/checkout@", workflow);
        Assert.DoesNotContain("ref:", workflow);
    }

    // A head as GitHub's commits API gives it: by default, update-branch's merge of main into the old head.
    private static string Commit(
        string verification = """{"verified": true, "reason": "valid"}""",
        string committer = """{"login": "web-flow"}""",
        string parents = $$"""[{"sha": "{{Old}}"}, {"sha": "{{Main}}"}]""") =>
        $$"""{"sha": "4444", "commit": {"message": "Merge branch 'main' into fix", "verification": {{verification}}}, "committer": {{committer}}, "parents": {{parents}}}""";

    // What the check prints for this commit, the old head being Old.
    private static string Check(string commit)
    {
        var start = new ProcessStartInfo("jq", ["-r", "--arg", "old", Old, "-f", Path.Combine(Root(), "tools", "github", "merge-of-main.jq")])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process jq = Process.Start(start) ?? throw new InvalidOperationException("jq did not start");
        jq.StandardInput.Write(commit);
        jq.StandardInput.Close();
        string output = jq.StandardOutput.ReadToEnd();
        string errors = jq.StandardError.ReadToEnd();
        jq.WaitForExit();
        Assert.True(jq.ExitCode == 0, $"jq failed ({jq.ExitCode}): {errors}");
        return output.Trim();
    }

    // The repository: the folder holding VERSION, from where the tests run.
    private static string Root()
    {
        for (DirectoryInfo dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "VERSION")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"no VERSION above {AppContext.BaseDirectory}");
    }
}
