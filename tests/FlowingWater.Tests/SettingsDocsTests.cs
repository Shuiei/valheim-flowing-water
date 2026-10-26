using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The settings as the documents tell them: the README's table names every setting the plugin binds, and the mod page
/// (which tells the main ones) names none that is not one.
/// </summary>
public class SettingsDocsTests
{
    // The longest a match may take (the files are the repository's own, short).
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(1);

    // A setting bound in Settings.cs: config.Bind("Section", "Key", ...).
    private static readonly Regex Bound = new("config\\.Bind\\(\"(?<section>\\w+)\", \"(?<key>\\w+)\"", RegexOptions.Compiled, Patience);

    // A setting named in a document's table: a row starting with `Section.Key` (or `Key`, for the top section's).
    private static readonly Regex Row = new("^\\| `(?<name>[\\w.]+)` \\|", RegexOptions.Compiled | RegexOptions.Multiline, Patience);

    [Fact]
    public void TheReadmeTellsEverySetting()
    {
        string[] bound = Settings();
        string[] told = Rows("README.md");

        Assert.NotEmpty(bound);
        Assert.Empty(bound.Except(told));
    }

    [Fact]
    public void TheModPageTellsOnlySettingsThatAre()
    {
        string[] told = Rows("thunderstore/README.md");

        Assert.Contains("Client.GpuWaterCell", told);
        Assert.Empty(told.Except(Settings()));
    }

    // The settings bound, as the documents name them: Section.Key, the top section's (General) by its key alone.
    private static string[] Settings() =>
        [.. Bound.Matches(File.ReadAllText(Repository.File("src/Settings.cs")))
            .Select(m => m.Groups["section"].Value == "General" ? m.Groups["key"].Value : $"{m.Groups["section"].Value}.{m.Groups["key"].Value}")];

    // The names in a document's tables that look like settings (with a section, or Enabled).
    private static string[] Rows(string path) =>
        [.. Row.Matches(File.ReadAllText(Repository.File(path)))
            .Select(m => m.Groups["name"].Value)
            .Where(n => n.Contains('.') || n == "Enabled")];
}
