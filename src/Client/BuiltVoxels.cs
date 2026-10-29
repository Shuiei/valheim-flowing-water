using System;
using System.Collections.Generic;
using FlowingWater.Volume;

namespace FlowingWater.Client;

/// <summary>
/// What is built, as the player's game reads it for the water on the card, without the game: a brick's voxels
/// rastered from the solids near it as the server rasters its own (<see cref="SolidRaster.Pieces"/>), kept as a byte a
/// voxel (<see cref="Solid"/> where the voxel is more than half solid, and the faces of its own a piece closes: east,
/// up, north), and written into the solids a 3D texture takes (<see cref="SolidsLayout"/>), each face closed on both
/// voxels it parts. So a wall or a roof thinner than a voxel holds the card's water wherever it stands, the server's
/// water near it or not (the server says what is built only in the voxels it has water in).
/// </summary>
internal static class BuiltVoxels
{
    /// <summary>A rastered brick's byte's bit for a voxel more than half solid.</summary>
    public const byte Solid = 128;

    /// <summary>The bits of the faces of its own a piece closes (as <see cref="Brick.ClosedX"/>, Y, Z: east, up, north).</summary>
    public const byte OwnFaces = Brick.ClosedX | Brick.ClosedY | Brick.ClosedZ;

    /// <summary>
    /// The brick at these brick coordinates rastered from these solids: a byte a voxel (by <see cref="Brick.Index"/>),
    /// null where none is solid or closes a face.
    /// </summary>
    public static byte[] Raster(int x, int y, int z, float voxelSize, IReadOnlyList<Solid> solids)
    {
        var brick = new Brick(x, y, z);
        SolidRaster.Pieces(brick, voxelSize, solids);
        byte[] voxels = null;
        for (int i = 0; i < Brick.Count; i++)
        {
            byte built = (byte)((brick.Solid[i] > 0.5f ? Solid : 0) | (brick.Closed[i] & OwnFaces));
            if (built != 0)
            {
                voxels ??= new byte[Brick.Count];
                voxels[i] = built;
            }
        }

        return voxels;
    }

    /// <summary>The brick a voxel is in, along an axis (voxel coordinates over 16, rounded down).</summary>
    public static int BrickOf(int voxel) => voxel >> 4;

    /// <summary>
    /// Writes the rastered bricks into solids laid out by <see cref="SolidsLayout.Index"/> (columns a side from voxel
    /// column x0, z0, <see cref="SolidsLayout.Rows"/> rows from voxel row baseRow): a solid voxel solid, each face a
    /// piece closes closed on the voxel on either side of it (east and west, up and down, north and south). Bits
    /// already there are kept. True if any was written.
    /// </summary>
    public static bool Write(IReadOnlyDictionary<(int X, int Y, int Z), byte[]> bricks, byte[] solids, int x0, int z0, int columns, int baseRow)
    {
        int rows = SolidsLayout.Rows;
        if (solids.Length != columns * rows * columns)
        {
            throw new ArgumentException($"solids for {columns} columns of {rows} rows are {columns * rows * columns} bytes, not {solids.Length}", nameof(solids));
        }

        bool any = false;

        // The voxels a row of the texture's own or one west, under or south of it: their faces of their own are the
        // texture's voxels' west, down and south faces.
        for (int by = BrickOf(baseRow - 1); by <= BrickOf(baseRow + rows - 1); by++)
        {
            for (int bz = BrickOf(z0 - 1); bz <= BrickOf(z0 + columns - 1); bz++)
            {
                for (int bx = BrickOf(x0 - 1); bx <= BrickOf(x0 + columns - 1); bx++)
                {
                    if (bricks.TryGetValue((bx, by, bz), out byte[] voxels) && voxels != null)
                    {
                        any |= WriteBrick(voxels, bx, by, bz, solids, x0, z0, columns, baseRow);
                    }
                }
            }
        }

        return any;
    }

    private static bool WriteBrick(byte[] voxels, int bx, int by, int bz, byte[] solids, int x0, int z0, int columns, int baseRow)
    {
        int rows = SolidsLayout.Rows;
        bool any = false;
        for (int y = 0; y < Brick.Size; y++)
        {
            int ty = (by * Brick.Size) + y - baseRow;
            if (ty < -1 || ty >= rows)
            {
                continue;
            }

            for (int z = 0; z < Brick.Size; z++)
            {
                int tz = (bz * Brick.Size) + z - z0;
                if (tz < -1 || tz >= columns)
                {
                    continue;
                }

                for (int x = 0; x < Brick.Size; x++)
                {
                    int tx = (bx * Brick.Size) + x - x0;
                    byte built = voxels[Brick.Index(x, y, z)];
                    if (built == 0 || tx < -1 || tx >= columns)
                    {
                        continue;
                    }

                    any |= Mark(solids, columns, tx, ty, tz, (byte)((built & Solid) != 0 ? SolidsLayout.Solid : 0));
                    if ((built & Brick.ClosedX) != 0)
                    {
                        any |= Mark(solids, columns, tx, ty, tz, SolidsLayout.ClosedEast);
                        any |= Mark(solids, columns, tx + 1, ty, tz, SolidsLayout.ClosedWest);
                    }

                    if ((built & Brick.ClosedY) != 0)
                    {
                        any |= Mark(solids, columns, tx, ty, tz, SolidsLayout.ClosedUp);
                        any |= Mark(solids, columns, tx, ty + 1, tz, SolidsLayout.ClosedDown);
                    }

                    if ((built & Brick.ClosedZ) != 0)
                    {
                        any |= Mark(solids, columns, tx, ty, tz, SolidsLayout.ClosedNorth);
                        any |= Mark(solids, columns, tx, ty, tz + 1, SolidsLayout.ClosedSouth);
                    }
                }
            }
        }

        return any;
    }

    // Sets the bits on the texture's voxel, if it is one of its (and there are bits): whether it did.
    private static bool Mark(byte[] solids, int columns, int x, int y, int z, byte bits)
    {
        if (bits == 0 || x < 0 || x >= columns || y < 0 || y >= SolidsLayout.Rows || z < 0 || z >= columns)
        {
            return false;
        }

        solids[SolidsLayout.Index(columns, x, y, z)] |= bits;
        return true;
    }
}
