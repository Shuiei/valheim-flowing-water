using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The game's sending out of its destroyed objects, found by name: found, each flush sends them out; missing, of
/// another shape or found twice (the game changed), it is never called nor throws, and the first flush says why, once.
/// </summary>
public class LateDestroysTests
{
    private readonly List<string> _errors = [];

    [Fact]
    public void FoundEachFlushSendsTheDestroyedOut()
    {
        var game = new Game();
        Action<Game> send = LateDestroys.Find<Action<Game>>(typeof(Game), "SendDestroyed", out string missing);
        var late = new LateDestroys(() => send(game), missing, _errors.Add);

        late.Flush();
        late.Flush();

        Assert.Null(missing);
        Assert.Equal(2, game.Sent);
        Assert.Empty(_errors);
    }

    [Fact]
    public void GoneItIsSaidOnce() => NotFound<Gone>("was not found");

    [Fact]
    public void TakingSomethingNowItIsNotCalledAndSaidOnce() => NotFound<Reshaped>("takes or gives other things");

    [Fact]
    public void FoundTwiceItIsNotCalledAndSaidOnce() => NotFound<Twins>("more than once");

    // Looked up on the stand-in T as on the game's (by name, an instance method taking nothing): none found, nothing
    // thrown, flushing twice says why once.
    private void NotFound<T>(string why)
    {
        Action<T> send = LateDestroys.Find<Action<T>>(typeof(T), "SendDestroyed", out string missing);
        var late = new LateDestroys(send == null ? null : () => throw new InvalidOperationException("called"), missing, _errors.Add);

        late.Flush();
        late.Flush();

        Assert.Null(send);
        string error = Assert.Single(_errors);
        Assert.Contains(why, error, StringComparison.Ordinal);
        Assert.Contains("read back at the next start", error, StringComparison.Ordinal);
    }

    // The game's manager of objects as it is: a private instance method taking nothing.
    private sealed class Game
    {
        public int Sent { get; private set; }

        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Found by name, as the game's.")]
        private void SendDestroyed() => Sent++;
    }

    // Changed: the method gone.
    private sealed class Gone
    {
    }

    // Changed: the method takes something now.
    private sealed class Reshaped
    {
        public int Sent { get; private set; }

        public void SendDestroyed(int count) => Sent += count;
    }

    // Changed: two methods of the name.
    private sealed class Twins
    {
        public int Sent { get; private set; }

        public void SendDestroyed() => Sent++;

        public void SendDestroyed(int count) => Sent += count;
    }
}
