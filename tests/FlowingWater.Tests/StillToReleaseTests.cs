using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The github-release job's first look (tools/release/still-to-release.sh), run in a git repository of its own with
/// stand-ins for GitHub's CLI and CircleCI's agent: a version not tagged yet is released; a rerun of a release made
/// from this commit, its zip on GitHub, stops the job there and goes on to the mod sites; the tag on another commit,
/// no release on GitHub, a release without its zip, or no zip to release, fail. Bash's, as in CI: skipped on Windows.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class StillToReleaseTests : IDisposable
{
    // The step is bash, as CI runs it; Windows has none to rely on (and no Unix file modes for the stand-ins).
    private const string NoBash = "Needs bash and Unix file modes: not on Windows";

    private const string Version = "1.2.3";

    private const string Zip = "FlowingWater-1.2.3.zip";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "flowingwater-release-" + Guid.NewGuid().ToString("N"));

    private readonly string _repo;

    private readonly string _dist;

    private readonly string _bin;

    // What the stand-ins were asked: gh's arguments, and the agent's.
    private readonly string _asked;

    private readonly string _head;

    /// <summary>Whether bash runs the step here: not on Windows (the tests are skipped there, as it is not CI's).</summary>
    public static bool HasBash => !OperatingSystem.IsWindows();

    public StillToReleaseTests()
    {
        _repo = Directory.CreateDirectory(Path.Combine(_folder, "repo")).FullName;
        _dist = Directory.CreateDirectory(Path.Combine(_folder, "dist")).FullName;
        _bin = Directory.CreateDirectory(Path.Combine(_folder, "bin")).FullName;
        _asked = Path.Combine(_folder, "asked");
        File.WriteAllText(Path.Combine(_dist, Zip), "zip");
        File.WriteAllText(Path.Combine(_repo, "VERSION"), Version + "\n");
        Git("init", "-q");
        Git("add", "VERSION");
        Git("commit", "-q", "-m", "one");
        Git("remote", "add", "origin", _repo);
        _head = Git("rev-parse", "HEAD").Trim();

        // gh release view answers the release's assets, one name a line (FAKE_ASSETS), or fails as for no release.
        StandIn("gh", "echo \"gh $*\" >> \"$ASKED\"\n[ -n \"${FAKE_ASSETS+set}\" ] || { echo 'release not found' >&2; exit 1; }\nprintf '%s\\n' \"$FAKE_ASSETS\"");
        StandIn("circleci-agent", "echo \"circleci-agent $*\" >> \"$ASKED\"");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact(Skip = NoBash, SkipUnless = nameof(HasBash))]
    public void AVersionNotTaggedYetIsReleased()
    {
        (int exit, string output) = Run();

        Assert.True(exit == 0, output);
        Assert.False(File.Exists(_asked), "GitHub and the agent were asked nothing");
    }

    [Theory(Skip = NoBash, SkipUnless = nameof(HasBash))]
    [InlineData(false)]
    [InlineData(true)]
    public void ARerunOfAReleaseMadeFromThisCommitGoesOnToTheSites(bool annotated)
    {
        Tag(annotated);

        (int exit, string output) = Run(Zip + "\nFlowingWater-1.2.3.zip.sigstore.json");

        Assert.True(exit == 0, output);
        Assert.Contains("released on GitHub already, from this commit", output);
        string asked = File.ReadAllText(_asked);
        Assert.Contains("gh release view v1.2.3 --repo Shuiei/valheim-flowing-water --json assets", asked);
        Assert.EndsWith("circleci-agent step halt\n", asked);
    }

    [Fact(Skip = NoBash, SkipUnless = nameof(HasBash))]
    public void TheTagOnAnotherCommitFails()
    {
        Tag(annotated: false);
        File.WriteAllText(Path.Combine(_repo, "VERSION"), Version + " \n");
        Git("commit", "-q", "-am", "two");

        (int exit, string output) = Run(Zip, sha: Git("rev-parse", "HEAD").Trim());

        Assert.Equal(1, exit);
        Assert.Contains($"v1.2.3 is tagged already, on {_head}", output);
        Assert.False(File.Exists(_asked), "GitHub and the agent were asked nothing");
    }

    [Fact(Skip = NoBash, SkipUnless = nameof(HasBash))]
    public void TheTagWithNoReleaseOnGitHubFails()
    {
        Tag(annotated: false);

        (int exit, string output) = Run(assets: null);

        Assert.Equal(1, exit);
        Assert.Contains("GitHub has no release for it: make it by hand", output);
        Assert.DoesNotContain("circleci-agent", File.ReadAllText(_asked));
    }

    [Fact(Skip = NoBash, SkipUnless = nameof(HasBash))]
    public void AReleaseWithoutItsZipFails()
    {
        Tag(annotated: false);

        (int exit, string output) = Run("FlowingWater-1.2.3.zip.sigstore.json\nFlowingWater-1.2.2.zip");

        Assert.Equal(1, exit);
        Assert.Contains("has no FlowingWater-1.2.3.zip: add it by hand", output);
        Assert.DoesNotContain("circleci-agent", File.ReadAllText(_asked));
    }

    [Fact(Skip = NoBash, SkipUnless = nameof(HasBash))]
    public void NoZipToReleaseFails()
    {
        File.Delete(Path.Combine(_dist, Zip));

        (int exit, string output) = Run();

        Assert.NotEqual(0, exit);
        Assert.Contains(Zip, output);
    }

    private void Tag(bool annotated)
    {
        if (annotated)
        {
            Git("tag", "-a", "-m", "v" + Version, "v" + Version);
        }
        else
        {
            Git("tag", "v" + Version);
        }
    }

    private void StandIn(string name, string body)
    {
        string path = Path.Combine(_bin, name);
        File.WriteAllText(path, "#!/usr/bin/env bash\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    // The script as the job runs it, from the repository's root, for this commit (or sha).
    private (int Exit, string Output) Run(string assets = null, string sha = null)
    {
        var start = Start("bash", Repository.File("tools/release/still-to-release.sh"), _dist);
        start.Environment["PATH"] = _bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["ASKED"] = _asked;
        start.Environment["CIRCLE_SHA1"] = sha ?? _head;
        start.Environment["CIRCLE_PROJECT_USERNAME"] = "Shuiei";
        start.Environment["CIRCLE_PROJECT_REPONAME"] = "valheim-flowing-water";
        start.Environment["GITHUB_TOKEN"] = "token";
        if (assets != null)
        {
            start.Environment["FAKE_ASSETS"] = assets;
        }

        return Finish(start);
    }

    private string Git(params string[] args)
    {
        (int exit, string output) = Finish(Start("git", args));
        Assert.True(exit == 0, $"git {string.Join(' ', args)}: {output}");
        return output;
    }

    // A command in the repository, git with no settings but these (none of the user's, no signing).
    private ProcessStartInfo Start(string command, params string[] args)
    {
        var start = new ProcessStartInfo(command) { WorkingDirectory = _repo, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "Test";
        start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "test@example.com";
        return start;
    }

    private static (int Exit, string Output) Finish(ProcessStartInfo start)
    {
        using var process = Process.Start(start);
        var error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromMinutes(1)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{start.FileName} {string.Join(' ', start.ArgumentList)} took over a minute");
        }

        return (process.ExitCode, output + error.Result);
    }
}
