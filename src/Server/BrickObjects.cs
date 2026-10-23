using System;
using System.Collections.Generic;
using System.Linq;
using FlowingWater.Volume;

namespace FlowingWater.Server;

/// <summary>What the game does with the bricks' objects, for <see cref="BrickObjects{TId}"/>.</summary>
/// <typeparam name="TId">An object's id.</typeparam>
internal interface IBrickWorld<TId>
{
    /// <summary>Whether the object is still there and still a brick (false once something else destroyed it).</summary>
    bool IsBrick(TId id);

    /// <summary>A new object where the brick is.</summary>
    TId Make(Brick brick);

    /// <summary>A brick's water written into its object, whoever has it.</summary>
    void Write(TId id, byte[] data);

    /// <summary>A brick's object destroyed.</summary>
    void Destroy(TId id);
}

/// <summary>
/// Each brick's object, kept by its id and looked up each time: the game hands a destroyed object out again as another
/// one, so only an id still a brick's is ever written or destroyed. A brick unchanged since last sent (a still pool
/// asleep) is neither read nor packed again; one whose water packs the same is not written again.
/// </summary>
/// <typeparam name="TId">An object's id.</typeparam>
internal sealed class BrickObjects<TId>
{
    private readonly IBrickWorld<TId> _world;

    private readonly Dictionary<long, TId> _objects = [];

    private readonly Dictionary<long, (Brick Brick, int Changes, int Hash)> _sent = [];

    /// <summary>Objects kept for the game's objects given.</summary>
    public BrickObjects(IBrickWorld<TId> world)
    {
        _world = world;
    }

    /// <summary>
    /// Sends every changed brick, and destroys the objects of the bricks that hold no water or are gone. Every brick's
    /// water is packed before any object is written: if that fails, the world's water is left whole as last sent, not
    /// some of it sent anew beside the rest as it was.
    /// </summary>
    /// <returns>The objects written.</returns>
    public int Publish(IEnumerable<Brick> bricks, float voxelSize)
    {
        var kept = new HashSet<long>();
        var packed = new List<(Brick Brick, long Key, byte[] Data, int Hash)>();
        foreach (Brick brick in bricks)
        {
            long key = VoxelGrid.Key(brick.X, brick.Y, brick.Z);
            if (Pack(brick, key, voxelSize, packed))
            {
                kept.Add(key);
            }
        }

        int written = 0;
        foreach ((Brick brick, long key, byte[] data, int hash) in packed)
        {
            if (data != null)
            {
                _world.Write(Object(brick, key), data);
                written++;
            }

            _sent[key] = (brick, brick.Changes, hash);
        }

        Forget([.. _objects.Keys.Where(k => !kept.Contains(k))]);
        return written;
    }

    /// <summary>Every brick's object destroyed and forgotten (the bricks are cut differently now).</summary>
    public void Drop() => Forget([.. _objects.Keys]);

    // A brick's water packed if it changed since last sent (its data null when it packs as last written); false when it
    // holds none. One made again at the same place since (forgotten dry, then wet again) is another brick.
    private bool Pack(Brick brick, long key, float voxelSize, List<(Brick, long, byte[], int)> packed)
    {
        bool live = _objects.TryGetValue(key, out TId known) && _world.IsBrick(known);
        bool sent = _sent.TryGetValue(key, out (Brick Brick, int Changes, int Hash) was) && was.Brick == brick;
        if (live && sent && was.Changes == brick.Changes)
        {
            return true;
        }

        if (!Array.Exists(brick.Water, w => w > Brick.Dry))
        {
            return false;
        }

        byte[] data = BrickData.Encode(brick, voxelSize);
        int hash = Hash(data);
        packed.Add((brick, key, !live || !sent || was.Hash != hash ? data : null, hash));
        return true;
    }

    // The brick's object, made (or made again, if something removed it) where the brick is.
    private TId Object(Brick brick, long key)
    {
        if (_objects.TryGetValue(key, out TId id) && _world.IsBrick(id))
        {
            return id;
        }

        id = _world.Make(brick);
        _objects[key] = id;
        return id;
    }

    private void Forget(List<long> keys)
    {
        foreach (long key in keys)
        {
            if (_world.IsBrick(_objects[key]))
            {
                _world.Destroy(_objects[key]);
            }

            _objects.Remove(key);
            _sent.Remove(key);
        }
    }

    private static int Hash(byte[] data)
    {
        int hash = data.Length;
        foreach (byte b in data)
        {
            hash = unchecked((hash * 31) + b);
        }

        return hash;
    }
}
