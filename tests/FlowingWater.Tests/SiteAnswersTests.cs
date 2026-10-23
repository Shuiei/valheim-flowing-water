using FlowingWater.Release;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// A rerun of the release on Hexium: a version an earlier run submitted (hidden, so not seen as there) is taken as
/// this release's, by the site's words; any other refusal, or the site's own failure, still fails the release.
/// </summary>
public class SiteAnswersTests
{
    [Theory]
    [InlineData(400, "{\"non_field_errors\":[\"Package of the same namespace, name and version already exists\"]}")]
    [InlineData(400, "{\"detail\":\"A package with this VERSION ALREADY exists.\"}")]
    [InlineData(409, "Version 0.15.10 already exists")]
    public void ASubmissionRefusedForAVersionThereAlreadyWasSubmittedBefore(int status, string answer) =>
        Assert.True(SiteAnswers.SubmittedAlready(status, answer));

    [Theory]
    [InlineData(500, "Package of the same namespace, name and version already exists")]
    [InlineData(502, "Bad gateway: version already exists")]
    [InlineData(301, "Package of the same namespace, name and version already exists")]
    public void TheSitesOwnFailureSaysNothingOfThePackage(int status, string answer) =>
        Assert.False(SiteAnswers.SubmittedAlready(status, answer));

    [Theory]
    [InlineData(400, "{\"upload_uuid\":[\"This upload has already been used\"]}")]
    [InlineData(400, "{\"version_number\":[\"Enter a valid version\"]}")]
    [InlineData(401, "Invalid token.")]
    [InlineData(400, "")]
    public void AnyOtherRefusalStillFails(int status, string answer) =>
        Assert.False(SiteAnswers.SubmittedAlready(status, answer));
}
