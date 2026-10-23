using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace FlowingWater.Tests;

/// <summary>
/// The pipeline's wiring, read from .circleci/continue.yml: the checks main's ruleset requires run on every pull
/// request's branch, and report at once on a release pull request; a release checks both mod sites before it tags
/// anything, and publishes on them only once the GitHub release is made.
/// </summary>
public class CircleCiTests
{
    // The checks main's ruleset on GitHub requires of a pull request (README.md, Continuous integration): set on
    // GitHub, not here, so this list follows it by hand.
    private static readonly string[] Required = ["code-analysis", "formatting", "package", "server-smoke", "unit-tests"];

    private static readonly string[] Sites = ["thunderstore", "hexium"];

    private readonly YamlMappingNode _pipeline = Load();

    [Fact]
    public void EveryPullRequestsBranchRunsTheRequiredChecks()
    {
        Dictionary<string, YamlMappingNode> jobs = Jobs("test");

        foreach (string check in Required)
        {
            Assert.True(jobs.TryGetValue(check, out YamlMappingNode job), $"the test workflow has no {check}");
            Assert.False(job.Children.ContainsKey("filters"), $"{check} runs on some branches only");
        }
    }

    [Fact]
    public void AReleasePullRequestReportsEveryRequiredCheck()
    {
        Dictionary<string, YamlMappingNode> jobs = Jobs("release-pr");

        Assert.Equal(Required.Order(), jobs.Keys.Order());
        Assert.All(jobs.Values, job => Assert.Equal("tested-on-main", Text(job, "type")));
    }

    [Fact]
    public void BothSitesAreCheckedWithoutPublishingOnceTheSmokeTestPassed()
    {
        Dictionary<string, YamlMappingNode> jobs = Jobs("release");

        foreach (string site in Sites)
        {
            YamlMappingNode check = jobs[$"check-{site}"];
            Assert.Equal("publish", Text(check, "type"));
            Assert.Equal(site, Text(check, "site"));
            Assert.Equal("--dry-run", Text(check, "options"));
            Assert.Equal("mod-token", Text(check, "context"));
            Assert.Equal(["server-smoke"], Requires(check));
        }
    }

    [Fact]
    public void NothingIsTaggedUntilBothSitesWouldTakeThePackage() =>
        Assert.Equal(["check-hexium", "check-thunderstore"], Requires(Jobs("release")["github-release"]).Order());

    [Fact]
    public void TheSitesArePublishedOnOnlyOnceTheGitHubReleaseIsMade()
    {
        Dictionary<string, YamlMappingNode> jobs = Jobs("release");

        foreach (string site in Sites)
        {
            YamlMappingNode publish = jobs[$"publish-{site}"];
            Assert.Equal("publish", Text(publish, "type"));
            Assert.Equal(site, Text(publish, "site"));
            Assert.False(publish.Children.ContainsKey("options"), $"publish-{site} only checks");
            Assert.Equal(["github-release"], Requires(publish));
        }
    }

    [Fact]
    public void ThePublishJobGivesPublishCsItsSiteAndOptions() =>
        Assert.Contains(
            "dotnet run --file tools/release/publish.cs -- << parameters.site >> /tmp/dist << parameters.options >>",
            Commands("publish"));

    [Fact]
    public void TheGitHubReleaseLooksWhetherItIsStillToBeReleasedBeforeItTagsAnything()
    {
        List<string> commands = Commands("github-release");

        int look = commands.FindIndex(c => c.StartsWith("tools/release/still-to-release.sh", StringComparison.Ordinal));
        int tag = commands.FindIndex(c => c.Contains("gh release create", StringComparison.Ordinal));
        Assert.InRange(look, 0, tag - 1);
    }

    private static YamlMappingNode Load()
    {
        var yaml = new YamlStream();
        using (var reader = new StreamReader(Repository.File(".circleci/continue.yml")))
        {
            yaml.Load(reader);
        }

        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }

    private static string Text(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(key, out YamlNode value) ? ((YamlScalarNode)value).Value : null;

    private static List<string> Requires(YamlMappingNode job) =>
        job.Children.TryGetValue("requires", out YamlNode list) ? [.. ((YamlSequenceNode)list).Select(n => ((YamlScalarNode)n).Value)] : [];

    // A workflow's jobs by the name each runs as (its name, else its job's), each with its job as "type" unless it is
    // an approval: "- package", "- package: {requires: ...}", "- publish: {name: check-hexium, ...}".
    private Dictionary<string, YamlMappingNode> Jobs(string workflow)
    {
        var jobs = new Dictionary<string, YamlMappingNode>();
        foreach (YamlNode entry in (YamlSequenceNode)Map(Map(_pipeline, "workflows"), workflow)["jobs"])
        {
            string job;
            YamlMappingNode settings;
            if (entry is YamlScalarNode plain)
            {
                job = plain.Value;
                settings = [];
            }
            else
            {
                KeyValuePair<YamlNode, YamlNode> only = ((YamlMappingNode)entry).Children.Single();
                job = ((YamlScalarNode)only.Key).Value;
                settings = (YamlMappingNode)only.Value;
            }

            string name = Text(settings, "name") ?? job;
            var named = new YamlMappingNode(settings.Children.Where(c => ((YamlScalarNode)c.Key).Value != "name"));
            if (Text(settings, "type") == null)
            {
                named.Add("type", job);
            }

            Assert.True(jobs.TryAdd(name, named), $"{workflow} runs two jobs as {name}");
        }

        return jobs;
    }

    // The commands of a job's run steps, in order.
    private List<string> Commands(string job)
    {
        var steps = (YamlSequenceNode)Map(Map(_pipeline, "jobs"), job)["steps"];
        return [.. steps.OfType<YamlMappingNode>()
            .Where(step => step.Children.ContainsKey("run"))
            .Select(step => Text(Map(step, "run"), "command").Trim())];
    }

    private static YamlMappingNode Map(YamlMappingNode node, string key) => (YamlMappingNode)node[key];
}
