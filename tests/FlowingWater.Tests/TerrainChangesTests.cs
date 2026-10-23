using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The players' terrain changes read as the game keeps them: applied over the generated heights within the game's
/// 8 m, a change that is not a number left out, and changes that cannot be read (not gzip, cut short, the wrong
/// size) leaving the whole zone as generated, said why, never thrown.
/// </summary>
public class TerrainChangesTests
{
    private const int Vertices = 65 * 65;

    private static readonly float[] Generated = [.. Enumerable.Range(0, Vertices).Select(i => 30f + (i % 7))];

    [Fact]
    public void ChangesAreAppliedOverTheGeneratedHeightsWithinTheGamesLimit()
    {
        var changes = new Dictionary<int, (float Level, float Smooth)> { [0] = (1.5f, 0.25f), [10] = (-3f, 0f), [20] = (20f, 0f), [30] = (-5f, -5f) };
        float[] heights = new float[Vertices];

        Assert.Null(TerrainChanges.Apply(Changes(changes), Generated, heights));

        Assert.Equal(Generated[0] + 1.75f, heights[0], 4);
        Assert.Equal(Generated[10] - 3f, heights[10], 4);
        Assert.Equal(Generated[20] + TerrainChanges.MaxChange, heights[20], 4);
        Assert.Equal(Generated[30] - TerrainChanges.MaxChange, heights[30], 4);
        Assert.Equal(Generated[5], heights[5]);
    }

    [Fact]
    public void AChangeThatIsNotANumberLeavesItsVertexAsGenerated()
    {
        var changes = new Dictionary<int, (float Level, float Smooth)> { [1] = (float.NaN, 0f), [2] = (0f, float.PositiveInfinity), [3] = (2f, 0f) };
        float[] heights = new float[Vertices];

        string problem = TerrainChanges.Apply(Changes(changes), Generated, heights);

        Assert.Equal("2 terrain changes are not numbers; the generated ground is kept there", problem);
        Assert.Equal(Generated[1], heights[1]);
        Assert.Equal(Generated[2], heights[2]);
        Assert.Equal(Generated[3] + 2f, heights[3], 4);
        Assert.All(heights, h => Assert.True(TerrainChanges.IsFinite(h)));
    }

    [Theory]
    [InlineData("not gzip")]
    [InlineData("cut in the header")]
    [InlineData("cut in the vertices")]
    [InlineData("too few vertices")]
    public void ChangesThatCannotBeReadLeaveTheWholeZoneAsGenerated(string how)
    {
        // Every vertex raised 2 m, then broken: none of it is kept, not even the vertices read before the break.
        byte[] whole = Changes(Enumerable.Range(0, Vertices).ToDictionary(i => i, _ => (2f, 0f)));
        byte[] data = how switch
        {
            "not gzip" => [1, 2, 3, 4, 5],
            "cut in the header" => Cut(whole, 10),
            "cut in the vertices" => Cut(whole, 4000),
            _ => Changes(new Dictionary<int, (float, float)> { [0] = (2f, 0f) }, vertices: 64 * 64),
        };
        float[] heights = [.. Enumerable.Repeat(-1f, Vertices)];

        string problem = TerrainChanges.Apply(data, Generated, heights);

        Assert.StartsWith("terrain changes unreadable (", problem);
        Assert.EndsWith("); using the generated ground", problem);
        Assert.Equal(Generated, heights);
    }

    [Fact]
    public void TheWrongSizeIsSaid()
    {
        string problem = TerrainChanges.Apply(Changes([], vertices: 10), Generated, new float[Vertices]);

        Assert.Contains($"they cover 10 vertices, expected {Vertices}", problem);
    }

    /// <summary>Changes as the game writes them (TerrainComp), gzipped: these vertices changed, the rest not, no paint.</summary>
    internal static byte[] Changes(Dictionary<int, (float Level, float Smooth)> changes, int vertices = Vertices) => Packed.Write(writer =>
    {
        writer.Write(1); // format version
        writer.Write(changes.Count); // operations
        writer.Write(10f); // last operation's point
        writer.Write(20f);
        writer.Write(30f);
        writer.Write(4f); // and radius
        writer.Write(vertices);
        for (int i = 0; i < vertices; i++)
        {
            bool changed = changes.TryGetValue(i, out (float Level, float Smooth) change);
            writer.Write(changed);
            if (changed)
            {
                writer.Write(change.Level);
                writer.Write(change.Smooth);
            }
        }

        writer.Write(0); // paint
    });

    // The bytes gzip made of the first this many bytes of what these unpack to.
    private static byte[] Cut(byte[] data, int length)
    {
        using var unpacked = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(new MemoryStream(data), System.IO.Compression.CompressionMode.Decompress))
        {
            gzip.CopyTo(unpacked);
        }

        byte[] start = unpacked.ToArray()[..length];
        return Packed.Write(writer => writer.Write(start));
    }
}
