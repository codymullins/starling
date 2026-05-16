using FluentAssertions;
using Tessera.Dom.Events;
using Xunit;

namespace Tessera.Dom.Tests;

public sealed class DomEventTests
{
    private static (Document doc, Element html, Element body, Element div, Element target) BuildTree()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        var div = doc.CreateElement("div");
        var target = doc.CreateElement("button");
        doc.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(div);
        div.AppendChild(target);
        return (doc, html, body, div, target);
    }

    [Fact]
    public void Dispatch_invokes_capture_then_target_then_bubble_in_order()
    {
        var (doc, html, body, div, target) = BuildTree();
        var log = new List<string>();

        doc.AddEventListener("click", _ => log.Add("doc-capture"), new AddEventListenerOptions(Capture: true));
        html.AddEventListener("click", _ => log.Add("html-capture"), new AddEventListenerOptions(Capture: true));
        body.AddEventListener("click", _ => log.Add("body-capture"), new AddEventListenerOptions(Capture: true));
        div.AddEventListener("click", _ => log.Add("div-capture"), new AddEventListenerOptions(Capture: true));
        target.AddEventListener("click", _ => log.Add("target"));
        div.AddEventListener("click", _ => log.Add("div-bubble"));
        body.AddEventListener("click", _ => log.Add("body-bubble"));
        html.AddEventListener("click", _ => log.Add("html-bubble"));
        doc.AddEventListener("click", _ => log.Add("doc-bubble"));

        target.DispatchEvent(new Event("click", new EventInit(Bubbles: true)));

        log.Should().Equal(
            "doc-capture", "html-capture", "body-capture", "div-capture",
            "target",
            "div-bubble", "body-bubble", "html-bubble", "doc-bubble");
    }

    [Fact]
    public void Event_without_bubbles_skips_bubble_phase()
    {
        var (doc, _, body, div, target) = BuildTree();
        var log = new List<string>();

        body.AddEventListener("focus", _ => log.Add("body-bubble"));
        div.AddEventListener("focus", _ => log.Add("div-bubble"));
        target.AddEventListener("focus", _ => log.Add("target"));

        target.DispatchEvent(new Event("focus"));

        log.Should().Equal("target");
    }

    [Fact]
    public void StopPropagation_halts_subsequent_ancestors()
    {
        var (doc, _, body, div, target) = BuildTree();
        var log = new List<string>();

        body.AddEventListener("click", _ => log.Add("body-bubble"));
        div.AddEventListener("click", e =>
        {
            log.Add("div-bubble");
            e.StopPropagation();
        });
        target.AddEventListener("click", _ => log.Add("target"));

        target.DispatchEvent(new Event("click", new EventInit(Bubbles: true)));

        log.Should().Equal("target", "div-bubble");
    }

    [Fact]
    public void StopImmediatePropagation_halts_remaining_listeners_on_same_target()
    {
        var (doc, _, _, _, target) = BuildTree();
        var log = new List<string>();

        target.AddEventListener("click", e =>
        {
            log.Add("first");
            e.StopImmediatePropagation();
        });
        target.AddEventListener("click", _ => log.Add("second"));

        target.DispatchEvent(new Event("click", new EventInit(Bubbles: true)));

        log.Should().Equal("first");
    }

    [Fact]
    public void Once_option_removes_listener_after_first_invocation()
    {
        var (doc, _, _, _, target) = BuildTree();
        var count = 0;
        target.AddEventListener("click", _ => count++, new AddEventListenerOptions(Once: true));

        target.DispatchEvent(new Event("click"));
        target.DispatchEvent(new Event("click"));
        target.DispatchEvent(new Event("click"));

        count.Should().Be(1);
    }

    [Fact]
    public void RemoveEventListener_takes_effect_for_subsequent_dispatches()
    {
        var (doc, _, _, _, target) = BuildTree();
        var count = 0;
        EventListener listener = _ => count++;

        target.AddEventListener("click", listener);
        target.DispatchEvent(new Event("click"));
        target.RemoveEventListener("click", listener).Should().BeTrue();
        target.DispatchEvent(new Event("click"));

        count.Should().Be(1);
    }

    [Fact]
    public void Duplicate_add_with_same_capture_flag_is_a_no_op()
    {
        var (doc, _, _, _, target) = BuildTree();
        var count = 0;
        EventListener listener = _ => count++;
        target.AddEventListener("click", listener);
        target.AddEventListener("click", listener);
        target.AddEventListener("click", listener);

        target.DispatchEvent(new Event("click"));

        count.Should().Be(1);
    }

    [Fact]
    public void Capture_and_bubble_registrations_with_same_callback_are_distinct()
    {
        var (doc, _, _, div, target) = BuildTree();
        var log = new List<string>();
        EventListener listener = e => log.Add($"{e.EventPhase}");

        div.AddEventListener("click", listener, new AddEventListenerOptions(Capture: true));
        div.AddEventListener("click", listener);

        target.DispatchEvent(new Event("click", new EventInit(Bubbles: true)));

        log.Should().Equal("CapturingPhase", "BubblingPhase");
    }

    [Fact]
    public void PreventDefault_only_works_on_cancelable_events()
    {
        var (doc, _, _, _, target) = BuildTree();

        var nonCancelable = new Event("click");
        target.AddEventListener("click", e => e.PreventDefault());
        var resultA = target.DispatchEvent(nonCancelable);
        resultA.Should().BeTrue();
        nonCancelable.DefaultPrevented.Should().BeFalse();

        var cancelable = new Event("click", new EventInit(Cancelable: true));
        var resultB = target.DispatchEvent(cancelable);
        resultB.Should().BeFalse();
        cancelable.DefaultPrevented.Should().BeTrue();
    }

    [Fact]
    public void Target_and_currentTarget_track_phase_correctly()
    {
        var (doc, _, body, div, target) = BuildTree();
        var capturedTargets = new List<(string Phase, object Target, object Current)>();

        body.AddEventListener("click", e =>
            capturedTargets.Add(("capture", e.Target!, e.CurrentTarget!)),
            new AddEventListenerOptions(Capture: true));
        target.AddEventListener("click", e =>
            capturedTargets.Add(("target", e.Target!, e.CurrentTarget!)));
        div.AddEventListener("click", e =>
            capturedTargets.Add(("bubble", e.Target!, e.CurrentTarget!)));

        target.DispatchEvent(new Event("click", new EventInit(Bubbles: true)));

        capturedTargets.Should().HaveCount(3);
        capturedTargets.Should().AllSatisfy(t => t.Target.Should().BeSameAs(target));
        capturedTargets[0].Current.Should().BeSameAs(body);
        capturedTargets[1].Current.Should().BeSameAs(target);
        capturedTargets[2].Current.Should().BeSameAs(div);
    }

    [Fact]
    public void Listener_exception_does_not_break_dispatch()
    {
        var (doc, _, _, _, target) = BuildTree();
        var reached = false;
        target.AddEventListener("click", _ => throw new InvalidOperationException("boom"));
        target.AddEventListener("click", _ => reached = true);

        var act = () => target.DispatchEvent(new Event("click"));

        act.Should().NotThrow();
        reached.Should().BeTrue();
    }

    [Fact]
    public void Custom_event_carries_detail_payload()
    {
        var (doc, _, _, _, target) = BuildTree();
        object? captured = null;
        target.AddEventListener("answer", e => captured = ((CustomEvent)e).Detail);

        target.DispatchEvent(new CustomEvent("answer") { Detail = 42 });

        captured.Should().Be(42);
    }

    [Fact]
    public void MouseEvent_keeps_coordinate_and_modifier_state()
    {
        var (doc, _, _, _, target) = BuildTree();
        MouseEvent? captured = null;
        target.AddEventListener("click", e => captured = (MouseEvent)e);

        target.DispatchEvent(new MouseEvent("click", new EventInit(Bubbles: true))
        {
            ClientX = 50, ClientY = 75, Button = 0, ShiftKey = true,
        });

        captured.Should().NotBeNull();
        captured!.ClientX.Should().Be(50);
        captured.ClientY.Should().Be(75);
        captured.ShiftKey.Should().BeTrue();
    }

    [Fact]
    public void Redispatching_in_flight_event_throws_inside_listener()
    {
        // Spec: dispatching an event whose dispatch flag is set throws
        // InvalidStateError. Listener exceptions are swallowed per the dispatch
        // algorithm, so we capture inside the listener to observe the throw.
        var (doc, _, _, _, target) = BuildTree();
        Exception? caught = null;
        target.AddEventListener("click", e =>
        {
            try { target.DispatchEvent(e); }
            catch (Exception ex) { caught = ex; }
        });

        target.DispatchEvent(new Event("click"));

        caught.Should().BeOfType<InvalidOperationException>();
    }
}
