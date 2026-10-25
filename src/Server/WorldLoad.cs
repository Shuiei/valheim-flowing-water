using System;
using System.Collections.Generic;
using FlowingWater.Volume;

namespace FlowingWater.Server;

/// <summary>
/// Puts the water saved with the world into the grid as it opens, without the game: a brick's as it was when the voxels
/// are the same size (each voxel's water back in its voxel, when that has room), else poured at each voxel's middle
/// into the new grid's voxels; an old column tile's poured on the ground at the middle of each of its cells. All of a
/// brick's or tile's water goes in or none of it does, and the bricks made only to look for room are forgotten again
/// when it does not: they would hold places under MaxBricks for nothing.
/// </summary>
internal sealed class WorldLoad
{
    private readonly VoxelGrid _grid;

    /// <summary>Loads water into this grid.</summary>
    public WorldLoad(VoxelGrid grid)
    {
        _grid = grid;
    }

    /// <summary>
    /// A saved brick's water into the grid, all or none: null (nothing poured) unless every voxel of water finds room,
    /// under the brick cap. Water back in the voxel it was in goes nowhere else: a voxel whose ceiling was at its very
    /// middle would otherwise have its water lifted onto the piece over it at every start.
    /// </summary>
    /// <returns>The cubic metres poured, or null.</returns>
    public float? PourView(BrickView view)
    {
        float s = view.VoxelSize;
        float volume = s * s * s;
        var water = new List<(Vec3 Point, float Volume)>();
        for (int i = 0; i < Brick.Count; i++)
        {
            if (view.Water[i] > Brick.Dry)
            {
                (int x, int y, int z) = Brick.Place(i);
                water.Add((new Vec3(((view.X * Brick.Size) + x + 0.5f) * s, ((view.Y * Brick.Size) + y + 0.5f) * s, ((view.Z * Brick.Size) + z + 0.5f) * s), view.Water[i] * volume));
            }
        }

        return Pour(water, Math.Abs(s - _grid.VoxelSize) < 1e-6f);
    }

    /// <summary>Water poured at points, all of it or none: null (nothing poured) unless each finds room, under the brick cap.</summary>
    /// <returns>The cubic metres poured, or null.</returns>
    public float? PourAll(List<(Vec3 Point, float Volume)> water) => Pour(water, false);

    /// <summary>
    /// An old column tile's water, each cell's at the ground in the cell's middle, a hair over it: what to pour.
    /// </summary>
    /// <param name="depths">Each cell's depth, metres (index z * side + x).</param>
    /// <param name="side">Cells along the tile's side.</param>
    /// <param name="originX">World x of the tile's west edge.</param>
    /// <param name="originZ">World z of its south edge.</param>
    /// <param name="zoneMetres">Metres along the tile's side.</param>
    /// <param name="ground">The ground's height at a point (world x, z).</param>
    public static List<(Vec3 Point, float Volume)> TileWater(float[] depths, int side, float originX, float originZ, float zoneMetres, Func<float, float, float> ground)
    {
        float cell = zoneMetres / side;
        var water = new List<(Vec3 Point, float Volume)>();
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                float depth = depths[(z * side) + x];
                if (depth > 0f)
                {
                    float wx = originX + ((x + 0.5f) * cell);
                    float wz = originZ + ((z + 0.5f) * cell);
                    water.Add((new Vec3(wx, ground(wx, wz) + 0.01f, wz), depth * cell * cell));
                }
            }
        }

        return water;
    }

    // Each point's water into the voxel it is in when that has room and inPlace says so, else into the voxel Room
    // finds for it; nothing unless every point finds one.
    private float? Pour(List<(Vec3 Point, float Volume)> water, bool inPlace)
    {
        int before = _grid.Version;
        var pours = new List<(int X, int Y, int Z, float Volume)>();
        foreach ((Vec3 point, float volume) in water)
        {
            if (!((inPlace && Fits(point, out int x, out int y, out int z)) || _grid.Room(point.X, point.Y, point.Z, out x, out y, out z)))
            {
                ForgetMadeSince(before);
                return null;
            }

            pours.Add((x, y, z, volume));
        }

        float total = 0f;
        foreach ((int x, int y, int z, float cubicMetres) in pours)
        {
            _grid.Pour(x, y, z, cubicMetres);
            total += cubicMetres;
        }

        return total;
    }

    // Whether the voxel a point is in has room for water: its brick can be made (under the cap, within reach) and it is
    // not solid.
    private bool Fits(Vec3 point, out int x, out int y, out int z)
    {
        float s = _grid.VoxelSize;
        x = (int)Math.Floor(point.X / s);
        y = (int)Math.Floor(point.Y / s);
        z = (int)Math.Floor(point.Z / s);
        return VoxelGrid.Within(Brick.Of(x), Brick.Of(y), Brick.Of(z))
            && _grid.Make(Brick.Of(x), Brick.Of(y), Brick.Of(z)) is { } brick
            && brick.Solid[Brick.Index(Brick.In(x), Brick.In(y), Brick.In(z))] < 1f;
    }

    // The bricks made since this version of the grid, forgotten: they were made only to look for room.
    private void ForgetMadeSince(int version)
    {
        var made = new List<Brick>();
        foreach (Brick brick in _grid.Bricks)
        {
            if (brick.MadeAt > version)
            {
                made.Add(brick);
            }
        }

        foreach (Brick brick in made)
        {
            _grid.Forget(brick);
        }
    }
}
