using System;
using System.Collections.Generic;
using System.Linq;
using FlowingWater.Server;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The bricks' objects: a changed brick written, an unchanged one left, a dry or gone one's object destroyed; an object
/// destroyed by something else never written nor destroyed, its brick given a new one; a failure while the
/// water is packed leaving every object as last sent, and everything sent the next time.
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

    private static Brick Wet(int x, int y, int z, float water)
    {
        var brick = new Brick(x, y, z);
        brick.Water[0] = water;
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

        public void Write(int id, byte[] data)
        {
            Assert.True(Objects.ContainsKey(id), $"{id} written, not a brick's");
            Objects[id] = data;
            Written.Add(id);
        }

        public void Destroy(int id)
        {
            Assert.True(Objects.Remove(id), $"{id} destroyed, not a brick's");
            Destroyed.Add(id);
        }
    }
}
