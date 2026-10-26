using System;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// What players built, for the particle water (<see cref="ParticleWater.SetSolids"/>): a byte a voxel over the card's
/// square at the voxel's size, <see cref="Rows"/> rows up from a base row, as a 3D texture: <see cref="Solid"/> where
/// the voxel is solid, and a bit for each of its faces a piece closes (east, west, up, down, north, south: a wall
/// thinner than a voxel).
/// GpuWater gathers them from the bricks near the camera (the server's reading of the pieces' colliders) on another
/// thread with the card's water, laid out by <see cref="Index"/>, and takes them onto the card on the main one
/// (<see cref="Take"/>). Needs only Unity, so the Unity project's preview fills one by hand.
/// </summary>
internal sealed class ParticleSolids : IDisposable
{
    /// <summary>Rows of voxels the texture holds: so many metres of height (at the voxel's size), round the camera.</summary>
    public const int Rows = 64;

    /// <summary>The byte's bit for a solid voxel; the six below it close its east, west, up, down, north and south faces.</summary>
    public const byte Solid = 128;

    /// <inheritdoc cref="Solid"/>
    public const byte ClosedEast = 1, ClosedWest = 2, ClosedUp = 4, ClosedDown = 8, ClosedNorth = 16, ClosedSouth = 32;

    private Texture3D _texture;

    /// <summary>The texture, once something was taken (else null).</summary>
    public Texture Texture => _texture;

    /// <summary>Voxel columns along a side of what was taken.</summary>
    public int Columns { get; private set; }

    /// <summary>The voxel's size (m) of what was taken.</summary>
    public float VoxelSize { get; private set; }

    /// <summary>World height of the bottom of the first row taken.</summary>
    public float BaseY { get; private set; }

    /// <summary>Whether anything taken is solid at all (nothing: the particles need not look).</summary>
    public bool Any { get; private set; }

    /// <summary>
    /// The index of a voxel in the bytes, as a 3D texture of columns wide, <see cref="Rows"/> high and columns deep lays
    /// them out: x fastest, then the row, then z.
    /// </summary>
    public static int Index(int columns, int x, int y, int z) => (((z * Rows) + y) * columns) + x;

    /// <summary>Solid voxels as <see cref="Index"/> lays them out, onto the card (the texture made, or made again at a new size).</summary>
    public void Take(byte[] solids, int columns, float voxelSize, int baseRow, bool any)
    {
        if (_texture == null || _texture.width != columns)
        {
            Free();
            _texture = new Texture3D(columns, Rows, columns, TextureFormat.R8, false)
            {
                name = "FlowingWater particle solids",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        _texture.SetPixelData(solids, 0);
        _texture.Apply(false);
        Columns = columns;
        VoxelSize = voxelSize;
        BaseY = baseRow * voxelSize;
        Any = any;
    }

    public void Dispose() => Free();

    // Gone: in the game as Unity frees things, in the editor's preview (not playing) at once, as it must there.
    private void Free()
    {
        if (_texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(_texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(_texture);
        }

        _texture = null;
    }
}
