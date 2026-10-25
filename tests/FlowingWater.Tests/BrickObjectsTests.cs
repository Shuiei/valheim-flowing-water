using System;
using System.Collections.Generic;
using System.Linq;
using FlowingWater.Server;
using FlowingWater.Tests.Volume;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The bricks' objects: a changed brick written, an unchanged one left, a dry or gone one's object destroyed; an object
/// destroyed by something else never written nor destroyed, its brick given a new one; a failure while the
/// water is packed leaving every object as last sent, and everything sent the next time; a send spread over calls
/// writing nothing until every brick is packed, packing as long as a frame's time lasts and on from there the next,
/// and given up for a whole send.
/// </summary>
public class BrickObjectsTests
{
    private const float VoxelSize = 0.5f;

    [Fact]
    public void AChangedBrickIsWrittenAndAnUnchangedOneIsNot()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick brick = Wet(0, 0, 0, 0.5f);

        Assert.Equal(1, objects.Publish([brick], VoxelSize));
        Assert.Equal([1], world.Written);
        Assert.Equal(0.5f, Water(world, 1), 2);

        // Unchanged: not packed again. Changed but packing the same (water moved and back): not written again.
        Assert.Equal(0, objects.Publish([brick], VoxelSize));
        brick.Changes++;
        Assert.Equal(0, objects.Publish([brick], VoxelSize));

        brick.Water[0] = 0.8f;
        brick.Changes++;
        Assert.Equal(1, objects.Publish([brick], VoxelSize));
        Assert.Equal([1, 1], world.Written);
        Assert.Equal(0.8f, Water(world, 1), 2);
        Assert.Equal(1, world.Made);
    }

    [Fact]
    public void AChangeThatWouldNotShowIsHeldBackUntilItAddsUp()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick brick = Wet(0, 0, 0, 0.5f);
        objects.Publish([brick], VoxelSize);

        // A step of 200 a voxel at a time: held back while within two of what was sent, sent at the third.
        foreach (float water in new[] { 0.505f, 0.51f })
        {
            brick.Water[0] = water;
            brick.Changes++;
            Assert.Equal(0, objects.Publish([brick], VoxelSize));
        }

        brick.Water[0] = 0.515f;
        brick.Changes++;
        Assert.Equal(1, objects.Publish([brick], VoxelSize));
        Assert.Equal(0.515f, Water(world, 1), 3);
    }

    [Fact]
    public void BeforeTheWorldIsSavedEveryChangeIsWrittenExactly()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick brick = Wet(0, 0, 0, 0.5f);
        objects.Publish([brick], VoxelSize);

        brick.Water[0] = 0.505f;
        brick.Changes++;
        Assert.Equal(1, objects.Publish([brick], VoxelSize, exact: true));
        Assert.Equal(0.505f, Water(world, 1), 3);
    }

    [Fact]
    public void AChangeHeldBackIsSentAfterTenSends()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick brick = Wet(0, 0, 0, 0.5f);
        objects.Publish([brick], VoxelSize);

        brick.Water[0] = 0.505f;
        int written = 0;
        for (int send = 0; send < BrickObjects<int>.RefreshSends; send++)
        {
            brick.Changes++;
            written += objects.Publish([brick], VoxelSize);
        }

        Assert.Equal(1, written);
        Assert.Equal(0.505f, Water(world, 1), 3);
    }

    [Fact]
    public void AChangeHeldBackIsWrittenThoughTheBrickChangesNoMore()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick brick = Wet(0, 0, 0, 0.5f);
        objects.Publish([brick], VoxelSize);

        // Held back, then saved: written exactly.
        brick.Water[0] = 0.505f;
        brick.Changes++;
        Assert.Equal(0, objects.Publish([brick], VoxelSize));
        Assert.Equal(1, objects.Publish([brick], VoxelSize, exact: true));
        Assert.Equal(0.505f, Water(world, 1), 3);

        // Held back, then left alone: written by the tenth send, and not again.
        brick.Water[0] = 0.51f;
        brick.Changes++;
        int written = 0;
        for (int send = 0; send < 2 * BrickObjects<int>.RefreshSends; send++)
        {
            written += objects.Publish([brick], VoxelSize);
            Assert.Equal(send < BrickObjects<int>.RefreshSends - 1 ? 0 : 1, written);
        }

        Assert.Equal(0.51f, Water(world, 1), 3);
    }

    [Fact]
    public void WaterComingAndACurrentChangingShowButATraceTooThinToDrawDoesNot()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick brick = Wet(0, 0, 0, 0.5f);
        objects.Publish([brick], VoxelSize);

        // In the dry voxel beside it, a trace thinner than a player's game draws (1 cm), then water it draws.
        brick.Water[1] = 0.004f;
        brick.Wets(1);
        Assert.Equal(0, objects.Publish([brick], VoxelSize));
        brick.Water[1] = 0.05f;
        brick.Changes++;
        Assert.Equal(1, objects.Publish([brick], VoxelSize));

        // The current through its east face: a little does not show, more does.
        float area = VoxelSize * VoxelSize;
        brick.FlowX[0] = 0.002f * area;
        brick.Changes++;
        Assert.Equal(0, objects.Publish([brick], VoxelSize));
        brick.FlowX[0] = 1f * area;
        brick.Changes++;
        Assert.Equal(1, objects.Publish([brick], VoxelSize));
    }

    [Fact]
    public void ARunningStreamIsSentLessOftenThanEverySecond()
    {
        // A stream down a slope into the sea, run for a minute and then sent once a second for half a minute: its
        // water stirs all the time, but most of it by less than shows.
        var grid = new VoxelGrid(1f, new TestSolids(0, TestSolids.Box(0, 47, 0, 47), (x, z) => 20f - (0.35f * z)));
        var solver = new VoxelSolver(grid) { SeaLevel = 5f };
        var objects = new BrickObjects<int>(new World { Anything = true });
        int written = 0;
        int changed = 0;
        var changes = new Dictionary<Brick, int>();
        for (int second = 0; second < 90; second++)
        {
            for (int tenth = 0; tenth < 10; tenth++)
            {
                grid.Pour(24, 20, 1, 0.05f);
                solver.Step(0.1f);
            }

            int sent = objects.Publish(grid.Bricks, 1f);
            if (second >= 60)
            {
                written += sent;
                changed += grid.Bricks.Count(b => !changes.TryGetValue(b, out int was) || was != b.Changes);
            }

            foreach (Brick brick in grid.Bricks)
            {
                changes[brick] = brick.Changes;
            }
        }

        // Of the bricks changed in each second, at most four in five were sent (seven in ten as measured).
        Assert.InRange(written, 1, changed * 4 / 5);
    }

    [Fact]
    public void AnObjectSavedWithTheWorldIsAdoptedWrittenAndNotMadeAgainAndOneWithNoBrickDestroyed()
    {
        var world = new World();
        world.Objects[7] = [];
        world.Objects[8] = [];
        var grid = new VoxelGrid(1f, new TestSolids(0));
        grid.Pour(0, 0, 0, 1f);
        var objects = new BrickObjects<int>(world);
        objects.Adopt(VoxelGrid.Key(0, 0, 0), 7);
        objects.Adopt(VoxelGrid.Key(5, 0, 0), 8);

        objects.Publish(grid.Bricks, 1f);

        Assert.Equal([7], world.Written);
        Assert.Equal(0, world.Made);
        Assert.Equal([8], world.Destroyed);
        Assert.Equal(1f, Water(world, 7), 2);
    }

    [Fact]
    public void AnObjectAdoptedWithItsBytesIsNotWrittenAgainUntilItsBrickChanges()
    {
        // The brick as saved, read back as it was: its first send writes nothing; a change writes it.
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick brick = Wet(0, 0, 0, 0.5f);
        byte[] data = BrickData.Encode(brick, 1f);
        world.Objects[7] = data;
        objects.Adopt(VoxelGrid.Key(0, 0, 0), 7, brick, data, 1f);

        Assert.Equal(0, objects.Publish([brick], 1f));
        Assert.Empty(world.Written);

        brick.Water[0] = 0.8f;
        brick.Changes++;
        Assert.Equal(1, objects.Publish([brick], 1f));
        Assert.Equal([7], world.Written);
        Assert.Equal(0, world.Made);
    }

    [Fact]
    public void ADryBrickAndOneGoneFromTheGridHaveTheirObjectsDestroyed()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick drying = Wet(0, 0, 0, 0.5f), going = Wet(1, 0, 0, 0.5f), staying = Wet(2, 0, 0, 0.5f);
        objects.Publish([drying, going, staying], VoxelSize);

        drying.Water[0] = 0f;
        drying.Changes++;
        Assert.Equal(0, objects.Publish([drying, staying], VoxelSize));

        Assert.Equal([1, 2], world.Destroyed.Order());
        Assert.Equal([3], world.Objects.Keys);

        // Wet again, the brick has a new object.
        drying.Water[0] = 0.5f;
        drying.Changes++;
        Assert.Equal(1, objects.Publish([drying, staying], VoxelSize));
        Assert.Equal(4, world.Made);
    }

    [Fact]
    public void AnObjectDestroyedBySomethingElseIsNeitherWrittenNorDestroyedAgain()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick still = Wet(0, 0, 0, 0.5f), moving = Wet(1, 0, 0, 0.5f), gone = Wet(2, 0, 0, 0.5f);
        objects.Publish([still, moving, gone], VoxelSize);

        // Another mod or an admin destroys all three. World.Write and World.Destroy fail the test on an id not a brick's.
        world.Objects.Clear();
        moving.Water[0] = 0.8f;
        moving.Changes++;

        // The still brick, though unchanged, and the moving one get new objects; the gone one is only forgotten.
        Assert.Equal(2, objects.Publish([still, moving], VoxelSize));
        Assert.Equal([4, 5], world.Objects.Keys.Order());
        Assert.Equal(0.5f, Water(world, 4), 2);
        Assert.Equal(0.8f, Water(world, 5), 2);
        Assert.Empty(world.Destroyed);

        // Later sends and drying go to the new objects only.
        still.Water[0] = 0f;
        still.Changes++;
        objects.Publish([still, moving], VoxelSize);
        Assert.Equal([4], world.Destroyed);
    }

    [Fact]
    public void ADropLeavesAnObjectNoLongerABricksAlone()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        objects.Publish([Wet(0, 0, 0, 0.5f), Wet(0, 1, 0, 0.5f)], VoxelSize);

        // The first's object destroyed by something else, or its id another kind of object's now.
        world.Objects.Remove(1);
        objects.Drop();
        Assert.Equal([2], world.Destroyed);
        Assert.Empty(world.Objects);
    }

    [Fact]
    public void ABrickMadeAgainAtTheSamePlaceIsSentAnew()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        objects.Publish([Wet(0, 0, 0, 0.5f)], VoxelSize);

        // Forgotten dry and wet again in the grid between two sends: the same water and changes, another brick.
        Assert.Equal(1, objects.Publish([Wet(0, 0, 0, 0.5f)], VoxelSize));
        Assert.Equal([1, 1], world.Written);
        Assert.Equal(1, world.Made);
    }

    [Fact]
    public void AFailureWhileTheWaterIsPackedLeavesEveryObjectAsLastSent()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick[] bricks = [Wet(0, 0, 0, 0.5f), Wet(1, 0, 0, 0.5f), Wet(2, 0, 0, 0.5f)];
        objects.Publish(bricks, VoxelSize);
        bricks[0].Water[0] = bricks[1].Water[0] = 0.8f;
        bricks[2].Water[0] = 0f;
        foreach (Brick brick in bricks)
        {
            brick.Changes++;
        }

        // The second brick's packing fails: no brick is written, and the dry one's object is not destroyed.
        world.Failing = 2;
        Assert.Throws<InvalidOperationException>(() => objects.Publish(bricks, VoxelSize));
        Assert.Equal([1, 2, 3], world.Written);
        Assert.Empty(world.Destroyed);
        Assert.All(world.Objects.Keys, id => Assert.Equal(0.5f, Water(world, id), 2));

        // Once it works again, every changed brick is sent: none was taken for sent.
        world.Failing = 0;
        Assert.Equal(2, objects.Publish(bricks, VoxelSize));
        Assert.Equal([3], world.Destroyed);
        Assert.All(world.Objects.Keys, id => Assert.Equal(0.8f, Water(world, id), 2));
    }

    [Fact]
    public void DroppedObjectsAreDestroyedAndMadeAnewOnTheNextSend()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick[] bricks = [Wet(0, 0, 0, 0.5f), Wet(0, 0, 1, 0.5f)];
        objects.Publish(bricks, VoxelSize);

        objects.Drop();
        Assert.Equal([1, 2], world.Destroyed.Order());
        Assert.Empty(world.Objects);

        // Unchanged, the bricks are still sent: their objects are gone.
        Assert.Equal(2, objects.Publish(bricks, VoxelSize));
        Assert.Equal([3, 4], world.Objects.Keys.Order());
    }

    [Fact]
    public void ASendSpreadOverCallsWritesNothingUntilEveryBrickIsPacked()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick[] bricks = [Wet(0, 0, 0, 0.5f), Wet(1, 0, 0, 0.5f), Wet(2, 0, 0, 0.5f)];
        objects.Publish(bricks, VoxelSize);
        foreach (Brick brick in bricks)
        {
            brick.Water[0] = 0.8f;
            brick.Changes++;
        }

        // A brick a call.
        objects.Start(bricks);
        Assert.Equal(0, objects.Send(VoxelSize, () => false));
        Assert.Equal(0, objects.Send(VoxelSize, () => false));
        Assert.True(objects.Sending);
        Assert.All(world.Objects.Keys, id => Assert.Equal(0.5f, Water(world, id), 2));

        // The first brick changes again after it was packed: sent as packed now, and again the next time.
        bricks[0].Water[0] = 0.3f;
        bricks[0].Changes++;
        Assert.Equal(3, objects.Send(VoxelSize, () => false));
        Assert.False(objects.Sending);
        Assert.All(world.Objects.Keys, id => Assert.Equal(0.8f, Water(world, id), 2));

        Assert.Equal(1, objects.Publish(bricks, VoxelSize));
        Assert.Equal(0.3f, Water(world, 1), 2);
    }

    [Fact]
    public void PackingStopsWhereTheFramesTimeEndsAndGoesOnFromThereNextFrame()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick[] bricks = [.. Enumerable.Range(0, 5).Select(x => Wet(x, 0, 0, 0.5f))];

        // A frame's time for two bricks: asked after each brick packed, it runs out at the second.
        int packed = 0;
        int frames = 0;
        bool TimeLeft() => ++packed % 2 != 0;

        objects.Start(bricks);
        int written = 0;
        while (objects.Sending)
        {
            written = objects.Send(VoxelSize, TimeLeft);
            frames++;
            Assert.Equal(Math.Min(frames * 2, bricks.Length), packed);
        }

        Assert.Equal(3, frames);
        Assert.Equal(5, written);
        Assert.Equal([1, 2, 3, 4, 5], world.Written);

        // A save sends everything at once, whatever a frame's time.
        foreach (Brick brick in bricks)
        {
            brick.Water[0] = 0.8f;
            brick.Changes++;
        }

        Assert.Equal(5, objects.Publish(bricks, VoxelSize));
        Assert.False(objects.Sending);
        Assert.All(world.Objects.Keys, id => Assert.Equal(0.8f, Water(world, id), 2));
    }

    [Fact]
    public void AWholeSendGivesUpTheOneUnderWay()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick going = Wet(0, 0, 0, 0.5f), staying = Wet(1, 0, 0, 0.5f);
        objects.Publish([going, staying], VoxelSize);

        // A send of both under way; the world is saved with one gone meanwhile: its object goes, and nothing is left
        // to the send given up.
        staying.Water[0] = 0.8f;
        staying.Changes++;
        objects.Start([going, staying]);
        objects.Send(VoxelSize, () => false);

        Assert.Equal(1, objects.Publish([staying], VoxelSize));
        Assert.False(objects.Sending);
        Assert.Equal([1], world.Destroyed);
        Assert.Equal(0, objects.Send(VoxelSize, () => true));
        Assert.Equal([1], world.Destroyed);
    }

    [Fact]
    public void AFailureGivesUpASpreadSend()
    {
        var world = new World();
        var objects = new BrickObjects<int>(world);
        Brick[] bricks = [Wet(0, 0, 0, 0.5f), Wet(1, 0, 0, 0.5f)];
        objects.Publish(bricks, VoxelSize);
        bricks[1].Water[0] = 0.8f;
        bricks[1].Changes++;

        objects.Start(bricks);
        objects.Send(VoxelSize, () => false);
        world.Failing = 2;
        Assert.Throws<InvalidOperationException>(() => objects.Send(VoxelSize, () => false));
        Assert.False(objects.Sending);
        Assert.Equal([1, 2], world.Written);
    }

    // A brick with water in its first voxel, taken into its wet voxels' box as a pour or a step would (a brick looks for
    // water only within it).
    private static Brick Wet(int x, int y, int z, float water)
    {
        var brick = new Brick(x, y, z);
        brick.Water[0] = water;
        brick.Wets(0);
        return brick;
    }

    private static float Water(World world, int id) => BrickData.Decode(world.Objects[id]).Water[0];

    // The game's bricks' objects: ids given out once, as the game does. Writing to or destroying an id not a brick's
    // (destroyed, or another kind of object's) fails the test.
    private sealed class World : IBrickWorld<int>
    {
        private int _next = 1;

        public Dictionary<int, byte[]> Objects { get; } = [];

        public List<int> Written { get; } = [];

        public List<int> Destroyed { get; } = [];

        public int Made => _next - 1;

        // An id whose lookup fails (0: none).
        public int Failing { get; set; }

        public bool IsBrick(int id) => id == Failing ? throw new InvalidOperationException("lookup failed") : Objects.ContainsKey(id);

        public int Make(Brick brick)
        {
            Objects[_next] = [];
            return _next++;
        }

        // Bricks' data written is not kept (a long run's bytes are not looked at).
        public bool Anything { get; init; }

        public void Write(int id, byte[] data)
        {
            Assert.True(Objects.ContainsKey(id), $"{id} written, not a brick's");
            Objects[id] = Anything ? [] : data;
            Written.Add(id);
        }

        public void Destroy(int id)
        {
            Assert.True(Objects.Remove(id), $"{id} destroyed, not a brick's");
            Destroyed.Add(id);
        }
    }
}
