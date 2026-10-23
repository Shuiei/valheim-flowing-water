using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The wait between two looks for sources and drains: the first at once, none while one is under way, and the next
/// the wait after the last one ended, however long that look took.
/// </summary>
public class LookWaitTests
{
    private const float Seconds = 3f;

    private const float Frame = 0.5f;

    [Fact]
    public void TheFirstLookIsDueAtOnce()
    {
        Assert.True(new LookWait().Due(Frame, Seconds, looking: false));
    }

    [Fact]
    public void ALookLongerThanTheWaitIsFollowedTheWaitAfterItsEndNotAtOnce()
    {
        var wait = new LookWait();
        Assert.True(wait.Due(Frame, Seconds, looking: false));
        wait.Began();

        // The look takes five seconds, longer than the wait: none is due meanwhile.
        for (int frame = 0; frame < 10; frame++)
        {
            Assert.False(wait.Due(Frame, Seconds, looking: true));
        }

        // Ended: the next is due three seconds of frames later, not at once.
        for (int frame = 1; frame < Seconds / Frame; frame++)
        {
            Assert.False(wait.Due(Frame, Seconds, looking: false), $"due {frame * Frame} s after the look ended");
        }

        Assert.True(wait.Due(Frame, Seconds, looking: false));
    }

    [Fact]
    public void ALookShorterThanTheWaitIsStillFollowedTheWholeWaitAfterItsEnd()
    {
        var wait = new LookWait();
        wait.Due(Frame, Seconds, looking: false);
        wait.Began();
        Assert.False(wait.Due(Frame, Seconds, looking: true));

        for (int frame = 1; frame < Seconds / Frame; frame++)
        {
            Assert.False(wait.Due(Frame, Seconds, looking: false));
        }

        Assert.True(wait.Due(Frame, Seconds, looking: false));
    }
}
