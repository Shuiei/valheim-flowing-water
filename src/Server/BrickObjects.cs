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

    /// <summary>A brick's object destroyed (the game may do it a little later: see <see cref="Flush"/>).</summary>
    void Destroy(TId id);

    /// <summary>The objects destroyed so far gone now, before the world is saved (else the save keeps them).</summary>
    void Flush();
}

/// <summary>
/// Each brick's object, kept by its id and looked up each time: the game hands a destroyed object out again as another
/// one, so only an id still a brick's is ever written or destroyed. A brick unchanged since last sent (a still pool
/// asleep) is neither read nor packed again; one whose change would not show (its water by a centimetre or so, its
/// current by a little: a running stream's water stirs all the time) is not packed either, until it adds up to one
/// that shows, or for <see cref="RefreshSends"/> sends at most; one whose water packs the same is not written again. A send may be spread
/// over frames (<see cref="Start"/>, <see cref="Send"/>), a few bricks packed at a time; nothing is written until every
/// brick of it is packed. An object saved with the world is taken over (<see cref="Adopt"/>) so that its brick's next
/// send writes it rather than make a second one beside it.
/// </summary>
/// <typeparam name="TId">An object's id.</typeparam>
internal sealed class BrickObjects<TId>
{
    /// <summary>Water steps (of 200 a voxel) a voxel's water may change by without the brick being sent again.</summary>
    public const int WaterSteps = 2;

    /// <summary>Current steps (of 127, square-root) a voxel's current may change by without the brick being sent again.</summary>
    public const int SpeedSteps = 3;

    /// <summary>Sends a changed brick may be held back for, its change not showing, before it is sent all the same.</summary>
    public const int RefreshSends = 10;

    /// <summary>
    /// Most objects a send writes, unless the world is about to be saved: the rest are written by the sends after it (a
    /// flood changing many bricks at once, or every brick at the first start of a version whose data says more, does not
    /// write the whole world's water in one frame).
    /// </summary>
    public const int MostWrites = 64;

    private readonly IBrickWorld<TId> _world;

    private readonly Dictionary<long, TId> _objects = [];

    // What each brick's object was last written with: its bytes and its look then, and the sends its changes were held
    // back since.
    private readonly Dictionary<long, Sent> _sent = [];

    // The send under way: its bricks, how many are packed, those holding water, and what they packed to (null: none
    // under way).
    private Brick[] _round;

    // Whether the send under way writes every change as it is (the world is about to be saved), none held back.
    private bool _exact;

    private int _packedCount;

    private readonly HashSet<long> _kept = [];

    // The bricks a send left for the next ones (MostWrites), by how many sends each has waited: the longest waiting are
    // written first, so the same first bricks of a round do not take every send's writes while a flood goes on.
    private readonly Dictionary<long, int> _waiting = [];

    // The bricks packed so far: what to write (null: its object keeps what it has) and what it was last sent as.
    private readonly List<(long Key, byte[] Data, Sent Sent)> _packed = [];

    /// <summary>Objects kept for the game's objects given.</summary>
    public BrickObjects(IBrickWorld<TId> world)
    {
        _world = world;
    }

    /// <summary>Whether a send is under way.</summary>
    public bool Sending => _round != null;

    /// <summary>
    /// Takes over an object saved with the world as this brick's: the brick's next send writes it, and the first send
    /// finding no brick there destroys it. When the bytes it holds are the brick as it is now (still, its water read
    /// back where it was), they are the brick's last sent, and it is not written again at the start; when they are not
    /// (some water moved as it was read back, the ground risen under it), its next send writes it.
    /// </summary>
    /// <param name="key">The brick's key.</param>
    /// <param name="id">The object.</param>
    /// <param name="brick">The brick read back from it.</param>
    /// <param name="data">The bytes it holds.</param>
    /// <param name="voxelSize">Metres along a voxel's side.</param>
    public void Adopt(long key, TId id, Brick brick, byte[] data, float voxelSize)
    {
        _objects[key] = id;
        byte[] look = BrickData.Look(brick, voxelSize);
        if (Same(data, BrickData.Encode(brick, voxelSize, look)))
        {
            _sent[key] = new Sent(brick, brick.Changes, data, look, 0);
        }
        else
        {
            _sent.Remove(key);
        }
    }

    /// <summary>
    /// Sends every changed brick now, and destroys the objects of the bricks that hold no water or are gone; a send
    /// under way is given up for it. Every brick's water is packed before any object is written: if that fails, the
    /// world's water is left whole as last sent, not some of it sent anew beside the rest as it was.
    /// </summary>
    /// <param name="bricks">The bricks.</param>
    /// <param name="voxelSize">Metres along a voxel's side.</param>
    /// <param name="exact">Every change written as it is, none held back for not showing: the world is about to be saved.</param>
    /// <returns>The objects written.</returns>
    public int Publish(IEnumerable<Brick> bricks, float voxelSize, bool exact = false)
    {
        Start(bricks, exact);
        return Send(voxelSize, () => true);
    }

    /// <summary>Starts a send of these bricks (as they are once packed), in place of any under way.</summary>
    /// <param name="bricks">The bricks.</param>
    /// <param name="exact">Every change written as it is, none held back for not showing.</param>
    public void Start(IEnumerable<Brick> bricks, bool exact = false)
    {
        _exact = exact;
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
        IEnumerable<(long Key, byte[] Data, Sent Sent)> order = _waiting.Count == 0 ? _packed : _packed.OrderByDescending(p => _waiting.TryGetValue(p.Key, out int waited) ? waited : 0);
        foreach ((long key, byte[] data, Sent sent) in order.ToList())
        {
            if (data != null)
            {
                if (!_exact && written >= MostWrites)
                {
                    // Left for the next send: not taken as sent, so it is packed and written again then (before those
                    // that waited less).
                    _waiting[key] = (_waiting.TryGetValue(key, out int waited) ? waited : 0) + 1;
                    continue;
                }

                _world.Write(Object(sent.Brick, key), data);
                written++;
            }

            _waiting.Remove(key);
            _sent[key] = sent;
        }

        foreach (long gone in _waiting.Keys.Where(k => !_kept.Contains(k)).ToList())
        {
            _waiting.Remove(gone);
        }

        Forget([.. _objects.Keys.Where(k => !_kept.Contains(k))]);

        // The world is about to be saved: the objects destroyed must be gone from it first, or the save keeps them, and
        // the next start reads them back (a dried brick's last water poured again, or a second object at a brick's place
        // beside the one made since).
        if (_exact)
        {
            _world.Flush();
        }

        return written;
    }

    /// <summary>Every brick's object destroyed and forgotten (the bricks are cut differently now); a send under way is given up.</summary>
    public void Drop()
    {
        _round = null;
        _waiting.Clear();
        Forget([.. _objects.Keys]);
    }

    // A brick's water packed if it changed since last sent, or a change is held back, and the change would show (its
    // data null when it is held back, or packs as last written: its object keeps what it has, and the look it was
    // written with is what the next change is weighed against); false when it holds none. A change held back is held
    // only so many sends, and never past a save, though the brick changes no more. One made again at the same place
    // since (forgotten dry, then wet again) is another brick.
    private bool Pack(Brick brick, long key, float voxelSize)
    {
        bool live = _objects.TryGetValue(key, out TId known) && _world.IsBrick(known);
        bool sent = _sent.TryGetValue(key, out Sent was) && was.Brick == brick;
        if (live && sent && was.Changes == brick.Changes && was.Held == 0)
        {
            _packed.Add((key, null, was));
            return true;
        }

        if (!brick.HoldsWater())
        {
            return false;
        }

        byte[] look = BrickData.Look(brick, voxelSize);
        if (!_exact && live && sent && was.Held + 1 < RefreshSends && !BrickData.Shows(was.Look, look, WaterSteps, SpeedSteps, BrickData.Steps(Drawing.HideDepth, voxelSize)))
        {
            _packed.Add((key, null, new Sent(brick, brick.Changes, was.Data, was.Look, was.Held + 1)));
            return true;
        }

        byte[] data = BrickData.Encode(brick, voxelSize, look);
        _packed.Add((key, !live || !sent || !Same(was.Data, data) ? data : null, new Sent(brick, brick.Changes, data, look, 0)));
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

    // Whether two bricks' bytes are the same (a hash of them was, once in a few billion, the same for different water).
    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    // What a brick's object was last written with: the brick (one made again at the same place is another), its count
    // of changes, its bytes and look, and the sends its changes were held back since.
    private sealed class Sent
    {
        public Sent(Brick brick, int changes, byte[] data, byte[] look, int held)
        {
            Brick = brick;
            Changes = changes;
            Data = data;
            Look = look;
            Held = held;
        }

        public Brick Brick { get; }

        public int Changes { get; }

        public byte[] Data { get; }

        public byte[] Look { get; }

        public int Held { get; }
    }
}
