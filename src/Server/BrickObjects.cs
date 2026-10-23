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
/// asleep) is neither read nor packed again; one whose water packs the same is not written again. A send may be spread
/// over frames (<see cref="Start"/>, <see cref="Send"/>), a few bricks packed at a time; nothing is written until every
/// brick of it is packed.
/// </summary>
/// <typeparam name="TId">An object's id.</typeparam>
internal sealed class BrickObjects<TId>
{
    private readonly IBrickWorld<TId> _world;

    private readonly Dictionary<long, TId> _objects = [];

    private readonly Dictionary<long, (Brick Brick, int Changes, int Hash)> _sent = [];

    // The send under way: its bricks, how many are packed, those holding water, and what they packed to (null: none
    // under way).
    private Brick[] _round;

    private int _packedCount;

    private readonly HashSet<long> _kept = [];

    private readonly List<(Brick Brick, long Key, byte[] Data, int Hash, int Changes)> _packed = [];

    /// <summary>Objects kept for the game's objects given.</summary>
    public BrickObjects(IBrickWorld<TId> world)
    {
        _world = world;
    }

    /// <summary>Whether a send is under way.</summary>
    public bool Sending => _round != null;

    /// <summary>
    /// Sends every changed brick now, and destroys the objects of the bricks that hold no water or are gone; a send
    /// under way is given up for it. Every brick's water is packed before any object is written: if that fails, the
    /// world's water is left whole as last sent, not some of it sent anew beside the rest as it was.
    /// </summary>
    /// <returns>The objects written.</returns>
    public int Publish(IEnumerable<Brick> bricks, float voxelSize)
    {
        Start(bricks);
        return Send(voxelSize, () => true);
    }

    /// <summary>Starts a send of these bricks (as they are once packed), in place of any under way.</summary>
    public void Start(IEnumerable<Brick> bricks)
    {
        _round = [.. bricks];
        _packedCount = 0;
        _kept.Clear();
        _packed.Clear();
    }

    /// <summary>
    /// Packs the send's bricks while <paramref name="timeLeft"/> says so (one at least); once all are packed, writes
    /// the changed ones and destroys the objects of the bricks that hold no water or are gone. Should packing fail, the
    /// send is given up: nothing of it is written.
    /// </summary>
    /// <returns>The objects written (none until the send's end).</returns>
    public int Send(float voxelSize, Func<bool> timeLeft)
    {
        if (_round == null)
        {
            return 0;
        }

        bool packed = false;
        try
        {
            while (_packedCount < _round.Length)
            {
                Brick brick = _round[_packedCount];
                long key = VoxelGrid.Key(brick.X, brick.Y, brick.Z);
                if (Pack(brick, key, voxelSize))
                {
                    _kept.Add(key);
                }

                _packedCount++;
                if (!timeLeft())
                {
                    break;
                }
            }

            packed = true;
        }
        finally
        {
            if (!packed)
            {
                _round = null;
            }
        }

        return _packedCount < _round.Length ? 0 : Write();
    }

    // The send's end: its changed bricks written, the objects of the others gone.
    private int Write()
    {
        _round = null;
        int written = 0;
        foreach ((Brick brick, long key, byte[] data, int hash, int changes) in _packed)
        {
            if (data != null)
            {
                _world.Write(Object(brick, key), data);
                written++;
            }

            _sent[key] = (brick, changes, hash);
        }

        Forget([.. _objects.Keys.Where(k => !_kept.Contains(k))]);
        return written;
    }

    /// <summary>Every brick's object destroyed and forgotten (the bricks are cut differently now); a send under way is given up.</summary>
    public void Drop()
    {
        _round = null;
        Forget([.. _objects.Keys]);
    }

    // A brick's water packed if it changed since last sent (its data null when it packs as last written); false when it
    // holds none. One made again at the same place since (forgotten dry, then wet again) is another brick.
    private bool Pack(Brick brick, long key, float voxelSize)
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
        _packed.Add((brick, key, !live || !sent || was.Hash != hash ? data : null, hash, brick.Changes));
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
