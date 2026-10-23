using System;

namespace FlowingWater.Release;

/// <summary>What a mod site's error answers mean to the release.</summary>
public static class SiteAnswers
{
    /// <summary>
    /// Whether a refused submission says the site has the package's name and version already (Thunderstore's words,
    /// which Hexium follows: "Package of the same namespace, name and version already exists"). On Hexium, whose
    /// hidden versions its API does not show, that is a version an earlier run of the release submitted: it is
    /// this release's. A site's own failure (5xx) says nothing of the package.
    /// </summary>
    /// <param name="status">The answer's HTTP status.</param>
    /// <param name="answer">What the site said.</param>
    public static bool SubmittedAlready(int status, string answer) =>
        status is >= 400 and < 500 && Says(answer, "already") && Says(answer, "version");

    private static bool Says(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
