using System;

namespace FlowingWater.Volume;

/// <summary>
/// Which voxel columns of the square the card draws, of those the server's water lets it (<see cref="SimColumns"/>):
/// none in a band along the square's edge, where its water is the server's as it is, a step a voxel on a slope (the
/// bricks draw it there, so the card's water meets theirs inside the square, never at its edge); and a column only once
/// it has let the card have it for <see cref="Settle"/> seconds, so the card's water there has settled to the server's
/// (a column new to the square, its ground just found, starts from the server's water, a step a voxel, and runs
/// smooth; drawn at once, that was seen as a ripple). Until then the bricks draw it, as before.
/// </summary>
internal sealed class SimHandover
{
    /// <summary>The band along the square's edge the card never draws, metres (at least two columns).</summary>
    public const float Margin = 4f;

    /// <summary>Seconds a column waits, the card moving its water, before the card draws it.</summary>
    public const float Settle = 3f;

    private static readonly float[] Never = [float.NaN];

    private readonly int _columns;
    private readonly int _margin;

    // Since when each column may be the card's (Unity's time, s), NaN where it may not.
    private float[] _since;

    /// <summary>For a square of <paramref name="columns"/> voxel columns of <paramref name="voxelSize"/> metres.</summary>
    public SimHandover(int columns, float voxelSize)
    {
        _columns = columns;
        _margin = Math.Max(2, (int)Math.Ceiling(Margin / voxelSize));
        _since = new float[columns * columns];
        for (int k = 0; k < _since.Length; k++)
        {
            _since[k] = float.NaN;
        }
    }

    /// <summary>Whether a column the server's water lets the card draw waits still (drawn by the bricks till then).</summary>
    public bool Waiting { get; private set; }

    /// <summary>
    /// The server's water as built for the card (<paramref name="texels"/> and <paramref name="layers"/>, as
    /// <see cref="SimColumns"/> gives them), as the card is to have it at <paramref name="now"/> (s): into
    /// <paramref name="drawnTexels"/> and <paramref name="drawnLayers"/>, the same but for the columns it does not draw
    /// yet or in the edge's band, which are not drawn (nor throw falls). The card's water there is moved as everywhere.
    /// </summary>
    public void Apply(float[] texels, float[] layers, float now, float[] drawnTexels, float[] drawnLayers)
    {
        Array.Copy(texels, drawnTexels, texels.Length);
        Array.Copy(layers, drawnLayers, layers.Length);
        Waiting = false;
        for (int vz = 0; vz < _columns; vz++)
        {
            for (int vx = 0; vx < _columns; vx++)
            {
                int k = (vz * _columns) + vx;
                bool lets = texels[(k * SimColumns.Texel) + 1] > 0.5f;
                bool inside = vx >= _margin && vz >= _margin && vx < _columns - _margin && vz < _columns - _margin;
                if (!lets || !inside)
                {
                    _since[k] = float.NaN;
                }
                else if (float.IsNaN(_since[k]))
                {
                    _since[k] = now;
                }

                if (!float.IsNaN(_since[k]) && now - _since[k] >= Settle)
                {
                    continue;
                }

                Waiting |= !float.IsNaN(_since[k]);
                drawnTexels[(k * SimColumns.Texel) + 1] = 0f;
                drawnLayers[(k * SimColumns.LayerTexel) + 2] = SimColumns.DryTop;
            }
        }
    }

    /// <summary>
    /// The square moved on by <paramref name="dx"/>, <paramref name="dz"/> columns: each column keeps its wait, those new
    /// to the square wait from when the card may have them.
    /// </summary>
    public void Shift(int dx, int dz)
    {
        _since = SimWindow.Shifted(_since, _columns, dx, dz, Never);
    }
}
