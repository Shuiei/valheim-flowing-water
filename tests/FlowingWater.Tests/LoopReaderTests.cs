using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>A clip's reader: samples handed out round the loop, however often it is asked, from where it is set.</summary>
public class LoopReaderTests
{
    [Fact]
    public void ReadsGoOnRoundTheLoopAndNeverFallSilent()
    {
        var reader = new LoopReader([1f, 2f, 3f]);
        float[] first = new float[4];
        float[] second = new float[4];

        reader.Read(first);
        reader.Read(second);

        Assert.Equal([1f, 2f, 3f, 1f], first);
        Assert.Equal([2f, 3f, 1f, 2f], second);
    }

    [Theory]
    [InlineData(1, 2f)]
    [InlineData(4, 2f)]
    [InlineData(-1, 3f)]
    public void ReadingStartsWhereItIsSetWrappedIntoTheLoop(int position, float first)
    {
        var reader = new LoopReader([1f, 2f, 3f]);
        float[] data = new float[1];

        reader.SetPosition(position);
        reader.Read(data);

        Assert.Equal(first, data[0]);
    }
}
