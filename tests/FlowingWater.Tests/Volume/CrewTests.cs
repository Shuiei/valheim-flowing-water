using System;
using System.Linq;
using System.Threading;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// A loop's bodies on the calling thread and the crew's helpers: each index run once, on the calling thread alone
/// with no helper, a body's exception thrown again on the calling thread once the others are done, and the calling
/// thread never held up by a helper busy elsewhere.
/// </summary>
public class CrewTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void EachIndexIsRunOnce(int helpers)
    {
        var crew = new Crew(helpers);
        for (int round = 0; round < 50; round++)
        {
            int[] runs = new int[100];
            crew.For(10, 90, i => Interlocked.Increment(ref runs[i]));

            Assert.All(runs.Take(10).Concat(runs.Skip(90)), r => Assert.Equal(0, r));
            Assert.All(runs.Skip(10).Take(80), r => Assert.Equal(1, r));
        }

        crew.For(5, 5, _ => throw new InvalidOperationException("no index"));
    }

    [Fact]
    public void WithNoHelperTheCallingThreadRunsEveryBody()
    {
        var crew = new Crew(0);
        int caller = Environment.CurrentManagedThreadId;

        crew.For(0, 20, _ => Assert.Equal(caller, Environment.CurrentManagedThreadId));
    }

    [Fact]
    public void ABodysExceptionIsThrownOnTheCallingThreadOnceTheOthersAreDone()
    {
        var crew = new Crew(3);
        int done = 0;

        var thrown = Assert.Throws<InvalidOperationException>(() => crew.For(0, 64, i =>
        {
            if (i == 7)
            {
                throw new InvalidOperationException("body 7");
            }

            Interlocked.Increment(ref done);
        }));

        Assert.Equal("body 7", thrown.Message);
        Assert.Equal(63, done);
    }

    [Fact]
    public void TheCallingThreadIsNotHeldUpByAHelperBusyElsewhere()
    {
        // The only helper takes a body of a first loop and is held there; a second loop, meanwhile, is all done by its
        // calling thread, without waiting for the helper.
        var crew = new Crew(1);
        using var helperIn = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = new Thread(() => crew.For(0, 2, _ =>
        {
            if (Thread.CurrentThread.Name?.StartsWith("FlowingWater crew", StringComparison.Ordinal) == true)
            {
                helperIn.Set();
                release.Wait();
            }
            else
            {
                helperIn.Wait();
            }
        }));
        first.Start();
        Assert.True(helperIn.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "the helper never took a body");

        int done = 0;
        var second = new Thread(() => crew.For(0, 100, _ => Interlocked.Increment(ref done)));
        second.Start();
        Assert.True(second.Join(TimeSpan.FromSeconds(10)), "the second loop waited for the busy helper");
        Assert.Equal(100, done);
        Assert.True(first.IsAlive);

        release.Set();
        Assert.True(first.Join(TimeSpan.FromSeconds(10)));
    }
}
