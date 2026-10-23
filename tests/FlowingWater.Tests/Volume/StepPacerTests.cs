using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// The water's steps paced over the server's frames: as many fixed steps as the frames owe while the budget's credit
/// lasts, the rest kept for later; a step dearer than the budget makes the next frames wait; a step not done is owed
/// again; owed time past the limit is given up.
/// </summary>
public class StepPacerTests
{
    [Fact]
    public void AFrameTakesTheStepsItOwesAndKeepsTheRest()
    {
        var pacer = new StepPacer(0.5f);
        pacer.Add(0.1f, 8.0);

        Assert.Equal(2, Steps(pacer, 0.04f, cost: 1.0));
        Assert.Equal(0.02f, pacer.Owed, 4);

        pacer.Add(0.02f, 8.0);
        Assert.Equal(1, Steps(pacer, 0.04f, cost: 1.0));
    }

    [Fact]
    public void StepsStopWhenTheFramesBudgetIsSpent()
    {
        var pacer = new StepPacer(0.5f);
        pacer.Add(0.4f, 8.0);

        Assert.Equal(3, Steps(pacer, 0.04f, cost: 3.0));
        Assert.Equal(0.28f, pacer.Owed, 4);
    }

    [Fact]
    public void AStepDearerThanTheBudgetMakesTheNextFramesWait()
    {
        // Each step costs 5 ms against 2 ms a frame: one frame in three steps, on average 2 ms a frame or less.
        var pacer = new StepPacer(10f);
        int steps = 0;
        for (int frame = 0; frame < 30; frame++)
        {
            pacer.Add(0.02f, 2.0);
            steps += Steps(pacer, 0.04f, cost: 5.0);
        }

        Assert.InRange(steps, 10, 12);
    }

    [Fact]
    public void TimeOwedPastTheLimitIsGivenUp()
    {
        var pacer = new StepPacer(0.5f);
        pacer.Add(0.3f, 8.0);
        pacer.Add(0.3f, 8.0);

        Assert.Equal(0.5f, pacer.Owed, 4);
        Assert.Equal(0.1f, pacer.TakeDropped(), 4);
        Assert.Equal(0f, pacer.TakeDropped());
    }

    [Fact]
    public void AStepNotDoneIsOwedAgain()
    {
        var pacer = new StepPacer(0.5f);
        pacer.Add(0.1f, 8.0);

        Assert.True(pacer.Take(0.04f));
        pacer.Return(0.04f);
        Assert.Equal(0.1f, pacer.Owed, 4);
    }

    [Fact]
    public void AllTheOwedTimeIsTakenAtOnce()
    {
        var pacer = new StepPacer(0.5f);
        pacer.Add(0.1f, 8.0);
        Assert.True(pacer.Take(0.04f));

        Assert.Equal(0.06f, pacer.TakeAll(), 4);
        Assert.Equal(0f, pacer.Owed);
        Assert.False(pacer.Take(0.01f));
    }

    [Fact]
    public void AStepReturnedWhileTheLimitIsOwedStaysWithinIt()
    {
        // A step taken spans frames that owe the limit again, then is not done: owed time stays at the limit, and
        // what went past is counted as given up.
        var pacer = new StepPacer(0.5f);
        pacer.Add(0.5f, 8.0);
        Assert.True(pacer.Take(0.04f));
        pacer.Add(0.04f, 8.0);
        Assert.Equal(0f, pacer.TakeDropped(), 4);

        pacer.Return(0.04f);

        Assert.Equal(0.5f, pacer.Owed, 4);
        Assert.Equal(0.04f, pacer.TakeDropped(), 4);
    }

    // Steps taken in one frame, each costing this many milliseconds, while credit is left (as the server does).
    private static int Steps(StepPacer pacer, float step, double cost)
    {
        int steps = 0;
        double spent = 0.0;
        while (pacer.Left(spent) > 0.0 && pacer.Take(step))
        {
            spent += cost;
            steps++;
        }

        pacer.Spent(spent);
        return steps;
    }
}
