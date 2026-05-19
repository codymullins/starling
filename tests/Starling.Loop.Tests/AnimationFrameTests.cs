using FluentAssertions;
using Xunit;

namespace Tessera.Loop.Tests;

public sealed class AnimationFrameTests
{
    [Fact]
    public void Raf_fires_once_on_next_frame_with_frame_timestamp()
    {
        var loop = new WebEventLoop();
        var fired = new List<double>();

        loop.RequestAnimationFrame(t => fired.Add(t));
        loop.PendingAnimationFrameCount.Should().Be(1);

        loop.RunFrame(16);
        fired.Should().Equal(16.0);
        loop.PendingAnimationFrameCount.Should().Be(0);

        loop.RunFrame(32);
        fired.Should().Equal(16.0); // no re-fire
    }

    [Fact]
    public void Nested_raf_schedules_for_next_frame_not_current()
    {
        var loop = new WebEventLoop();
        var fired = new List<double>();

        loop.RequestAnimationFrame(t =>
        {
            fired.Add(t);
            loop.RequestAnimationFrame(t2 => fired.Add(t2));
        });

        loop.RunFrame(16);
        fired.Should().Equal(16.0); // nested didn't run this frame

        loop.RunFrame(32);
        fired.Should().Equal(16.0, 32.0);
    }

    [Fact]
    public void Cancel_before_fire_removes_callback()
    {
        var loop = new WebEventLoop();
        var fired = new List<int>();

        var a = loop.RequestAnimationFrame(_ => fired.Add(1));
        loop.RequestAnimationFrame(_ => fired.Add(2));
        loop.CancelAnimationFrame(a).Should().BeTrue();

        loop.RunFrame(16);
        fired.Should().Equal(2);
    }

    [Fact]
    public void All_callbacks_in_one_frame_see_same_timestamp()
    {
        var loop = new WebEventLoop();
        var stamps = new List<double>();

        loop.RequestAnimationFrame(t => stamps.Add(t));
        loop.RequestAnimationFrame(t => stamps.Add(t));
        loop.RequestAnimationFrame(t => stamps.Add(t));

        loop.RunFrame(42);
        stamps.Should().Equal(42.0, 42.0, 42.0);
    }

    [Fact]
    public void Run_frame_drains_due_timers_before_raf()
    {
        var loop = new WebEventLoop();
        var log = new List<string>();

        loop.SetTimeout(() => log.Add("timer"), 10);
        loop.RequestAnimationFrame(_ => log.Add("raf"));

        loop.RunFrame(16);
        log.Should().Equal("timer", "raf");
    }

    [Fact]
    public void Run_frame_microtasks_drain_between_raf_callbacks()
    {
        var loop = new WebEventLoop();
        var log = new List<string>();

        loop.RequestAnimationFrame(_ =>
        {
            log.Add("raf1");
            loop.QueueMicrotask(() => log.Add("mt1"));
        });
        loop.RequestAnimationFrame(_ => log.Add("raf2"));

        loop.RunFrame(16);
        log.Should().Equal("raf1", "mt1", "raf2");
    }

    [Fact]
    public void Run_frame_rejects_backwards_time()
    {
        var loop = new WebEventLoop();
        loop.RunFrame(50);
        var act = () => loop.RunFrame(40);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Advance_by_now_fires_raf_callbacks()
    {
        // AdvanceBy is implemented in terms of RunFrame for back-compat,
        // so a pending rAF should now fire as part of the time advance.
        var loop = new WebEventLoop();
        var fired = new List<double>();

        loop.RequestAnimationFrame(t => fired.Add(t));
        loop.AdvanceBy(16);
        fired.Should().Equal(16.0);
    }
}
