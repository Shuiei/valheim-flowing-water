using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The reflection probe what is drawn over the whole screen is lit by (<see cref="ProbePick"/>): of those whose box
/// holds the camera, the widest; none when none holds it (Unity's default sky then); chosen anew at each place, so moving
/// out of a probe's box leaves it.
/// </summary>
public class ProbePickTests
{
    [Fact]
    public void OfTheProbesHoldingThePlaceTheWidestIsTaken()
    {
        bool[] holds = [true, true, false];
        float[] area = [46f, 4_000_000f, 9_000_000f];

        Assert.Equal(1, ProbePick.Widest(3, k => holds[k], k => area[k]));
    }

    [Fact]
    public void NoneHoldingThePlaceTakesNone()
    {
        Assert.Equal(-1, ProbePick.Widest(2, _ => false, _ => 100f));
        Assert.Equal(-1, ProbePick.Widest(0, _ => true, _ => 100f));
    }

    [Fact]
    public void AProbeOfNoSizeIsNoneToTake()
    {
        Assert.Equal(-1, ProbePick.Widest(1, _ => true, _ => 0f));
    }

    [Fact]
    public void MovingOutOfTheWidestsBoxTakesTheNextOrNone()
    {
        float[] area = [100f, 50f];
        bool[] here = [true, true];

        Assert.Equal(0, ProbePick.Widest(2, k => here[k], k => area[k]));
        here[0] = false;
        Assert.Equal(1, ProbePick.Widest(2, k => here[k], k => area[k]));
        here[1] = false;
        Assert.Equal(-1, ProbePick.Widest(2, k => here[k], k => area[k]));
    }
}
