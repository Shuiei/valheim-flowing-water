using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// The ground under the water moved on the graphics card, found as the game's physics finds it: rays cast straight down
/// onto the terrain and what players built (not the water, characters, ships nor items), a few rows of cells at a
/// time on the game's worker threads, read the frame after. Cells with no water near them are not cast (a wall: no
/// water reaches them); a ray that meets nothing (it started under the ground) is a wall too.
/// </summary>
internal sealed class BedCast : IDisposable
{
    /// <summary>The ground of a cell that is not cast, or where the ray met nothing: no water goes there.</summary>
    public const float Wall = 10000f;

    // How far down a ray looks, metres.
    private const float Reach = 64f;

    // What the ground is: the terrain and the solid parts of the world and of what is built.
    private static readonly string[] SolidLayers = ["Default", "static_solid", "Default_small", "piece", "terrain"];

    private readonly int _cells;

    private readonly int _mask = LayerMask.GetMask(SolidLayers);

    private NativeArray<RaycastCommand> _commands;

    private NativeArray<RaycastHit> _hits;

    // Which of the rows' cells were cast (the others are walls), and the rows of the batch under way.
    private bool[] _cast = [];

    private JobHandle _job;

    private int _row = -1;

    private int _rows;

    /// <summary>Casts for a square of <paramref name="cells"/> cells a side.</summary>
    public BedCast(int cells) => _cells = cells;

    /// <summary>Whether a batch is under way.</summary>
    public bool Busy => _row >= 0;

    /// <summary>
    /// Casts <paramref name="rows"/> rows of cells from row <paramref name="row"/>, each cell from the height
    /// <paramref name="from"/> gives (x, z in cells; NaN: not cast), at the world places of the square's cells (from its
    /// south-west corner, <paramref name="cellSize"/> metres each).
    /// </summary>
    public void Start(int row, int rows, Func<int, int, float> from, Vector2 corner, float cellSize)
    {
        if (Busy)
        {
            throw new InvalidOperationException("a batch is still under way");
        }

        int count = rows * _cells;
        if (!_commands.IsCreated || _commands.Length < count)
        {
            Free();
            _commands = new NativeArray<RaycastCommand>(count, Allocator.Persistent);
            _hits = new NativeArray<RaycastHit>(count, Allocator.Persistent);
            _cast = new bool[count];
        }

        var query = new QueryParameters(_mask, false, QueryTriggerInteraction.Ignore, false);
        for (int r = 0; r < rows; r++)
        {
            for (int x = 0; x < _cells; x++)
            {
                int k = (r * _cells) + x;
                float height = from(x, row + r);
                _cast[k] = !float.IsNaN(height);
                Vector3 origin = new(corner.x + ((x + 0.5f) * cellSize), _cast[k] ? height : 0f, corner.y + ((row + r + 0.5f) * cellSize));

                // A cell not cast still has a command (the batch is whole): a ray nowhere, of no length.
                _commands[k] = new RaycastCommand(origin, Vector3.down, query, _cast[k] ? Reach : 0f);
            }
        }

        _job = RaycastCommand.ScheduleBatch(_commands.GetSubArray(0, count), _hits.GetSubArray(0, count), 64, 1);
        _row = row;
        _rows = rows;
    }

    /// <summary>
    /// The batch's ground, once its rays are cast (it waits for them then): written into <paramref name="ground"/>
    /// (the square's cells, a row after another) and given back as its first row and row count; false while none is
    /// under way.
    /// </summary>
    public bool Finish(float[] ground, out int row, out int rows)
    {
        row = _row;
        rows = _rows;
        if (!Busy)
        {
            return false;
        }

        _job.Complete();
        for (int r = 0; r < rows; r++)
        {
            for (int x = 0; x < _cells; x++)
            {
                int k = (r * _cells) + x;
                RaycastHit hit = _hits[k];
                ground[((row + r) * _cells) + x] = _cast[k] && hit.colliderInstanceID != 0 ? hit.point.y : Wall;
            }
        }

        _row = -1;
        return true;
    }

    public void Dispose()
    {
        if (Busy)
        {
            _job.Complete();
            _row = -1;
        }

        Free();
    }

    private void Free()
    {
        if (_commands.IsCreated)
        {
            _commands.Dispose();
        }

        if (_hits.IsCreated)
        {
            _hits.Dispose();
        }
    }
}
