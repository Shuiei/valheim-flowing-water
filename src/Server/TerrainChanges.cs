using System;
using System.IO;

namespace FlowingWater.Server;

/// <summary>
/// The players' terrain changes in a zone, read without the game from the bytes its terrain-changes object keeps
/// (gzip of the format version, the operation count, the last operation's point and radius, then for each vertex
/// whether it changed and, if so, its level and smoothing deltas; its paint after, not read), and applied over the
/// generated heights within the game's limit.
/// </summary>
internal static class TerrainChanges
{
    /// <summary>The game clamps a vertex to this far from its generated height, whatever the tools did.</summary>
    public const float MaxChange = 8f;

    /// <summary>
    /// The heights the changes make over the generated ones. Changes that cannot be read leave the whole zone as
    /// generated; a change that is not a number leaves its vertex as generated (it would make the water's solids none
    /// too, and the water with them).
    /// </summary>
    /// <param name="data">The changes, as the game keeps them.</param>
    /// <param name="generated">The generated heights.</param>
    /// <param name="heights">Set to the heights with the changes, as many as <paramref name="generated"/>.</param>
    /// <returns>What was wrong with the changes, for the log; null when there was nothing.</returns>
    public static string Apply(byte[] data, float[] generated, float[] heights)
    {
        generated.CopyTo(heights, 0);
        int bad;
        try
        {
            bad = Packed.Read(data, "terrain changes", reader => Read(reader, generated, heights));
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            // Not gzip, cut short, or not the game's format (InvalidDataException); a stream Mono's gzip reader finds
            // broken (IOException).
            generated.CopyTo(heights, 0);
            return $"terrain changes unreadable ({e.Message}); using the generated ground";
        }

        return bad == 0 ? null : $"{bad} terrain changes are not numbers; the generated ground is kept there";
    }

    /// <summary>Whether a height is a number, and finite.</summary>
    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    // The changes read over the heights; how many were not numbers.
    private static int Read(BinaryReader reader, float[] generated, float[] heights)
    {
        reader.ReadInt32(); // format version
        reader.ReadInt32(); // operation count
        reader.ReadSingle(); // last operation's point
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadSingle(); // and radius
        int count = reader.ReadInt32();
        if (count != heights.Length)
        {
            throw new InvalidDataException($"they cover {count} vertices, expected {heights.Length}");
        }

        int bad = 0;
        for (int i = 0; i < count; i++)
        {
            if (!reader.ReadBoolean())
            {
                continue;
            }

            float level = reader.ReadSingle();
            float smooth = reader.ReadSingle();
            if (!IsFinite(level + smooth))
            {
                bad++;
                continue;
            }

            float height = generated[i] + level + smooth;
            heights[i] = Math.Min(Math.Max(height, generated[i] - MaxChange), generated[i] + MaxChange);
        }

        return bad;
    }
}
