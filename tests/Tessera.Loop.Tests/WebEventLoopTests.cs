using FluentAssertions;
using Xunit;

namespace Tessera.Loop.Tests;

public sealed class WebEventLoopTests
{
    [Fact]
    public void Microtasks_run_before_due_timers()
    {
        var loop = new WebEventLoop();
        var log = new List<string>();

        loop.SetTimeout(() => log.Add("timer"), 0);
        loop.QueueMicrotask(() => log.Add("microtask"));

        loop.RunUntilIdle();

        log.Should().Equal("microtask", "timer");
    }

    [Fact]
    public void Timers_wait_until_time_advances_and_can_be_cleared()
    {
        var loop = new WebEventLoop();
        var log = new List<string>();

        loop.SetTimeout(() => log.Add("late"), 10);
        var cancelled = loop.SetTimeout(() => log.Add("cancelled"), 5);
        loop.ClearTimeout(cancelled).Should().BeTrue();

        loop.AdvanceBy(5);
        log.Should().BeEmpty();

        loop.AdvanceBy(5);
        log.Should().Equal("late");
    }
}
