using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// B5-3-followup-a — pins the <see cref="JsRuntime.WithActiveVm"/> contract:
/// <list type="bullet">
///   <item>the helper publishes <c>realm.ActiveVm</c> for the duration of
///   <c>body</c> and restores the previous value on exit;</item>
///   <item>nested invocations keep the outer VM visible after the inner one
///   returns;</item>
///   <item>exceptions thrown from <c>body</c> still restore the prior
///   <c>ActiveVm</c> via the helper's try/finally.</item>
/// </list>
/// </summary>
[TestClass]
public class WithActiveVmTests
{
    [TestMethod]
    public void WithActiveVm_publishes_and_restores_ActiveVm()
    {
        var runtime = new JsRuntime();
        var realm = runtime.Realm;

        // No script frame on the stack yet — ActiveVm should be null.
        realm.ActiveVm.Should().BeNull();

        JsVm? observed = null;
        runtime.WithActiveVm(() =>
        {
            observed = realm.ActiveVm;
        });

        observed.Should().NotBeNull("WithActiveVm must publish a VM during body");
        realm.ActiveVm.Should().BeNull("WithActiveVm must restore the previous null on exit");
    }

    [TestMethod]
    public void WithActiveVm_nested_calls_preserve_outer_vm_on_exit()
    {
        var runtime = new JsRuntime();
        var realm = runtime.Realm;

        JsVm? outerSeenBeforeInner = null;
        JsVm? innerVm = null;
        JsVm? outerSeenAfterInner = null;

        runtime.WithActiveVm(() =>
        {
            outerSeenBeforeInner = realm.ActiveVm;
            runtime.WithActiveVm(() =>
            {
                innerVm = realm.ActiveVm;
            });
            outerSeenAfterInner = realm.ActiveVm;
        });

        outerSeenBeforeInner.Should().NotBeNull();
        innerVm.Should().BeSameAs(outerSeenBeforeInner,
            "the inner call should reuse the outer VM (previous is non-null)");
        outerSeenAfterInner.Should().BeSameAs(outerSeenBeforeInner,
            "exiting the inner WithActiveVm must not clear the outer VM");
        realm.ActiveVm.Should().BeNull();
    }

    [TestMethod]
    public void WithActiveVm_restores_previous_vm_when_body_throws()
    {
        var runtime = new JsRuntime();
        var realm = runtime.Realm;

        realm.ActiveVm.Should().BeNull();

        var action = () => runtime.WithActiveVm(() =>
        {
            realm.ActiveVm.Should().NotBeNull("VM is published during body");
            throw new InvalidOperationException("boom");
        });

        action.Should().Throw<InvalidOperationException>().WithMessage("boom");
        realm.ActiveVm.Should().BeNull("the finally block must restore the prior ActiveVm");
    }

    [TestMethod]
    public void WithActiveVm_drains_microtasks_after_body()
    {
        var runtime = new JsRuntime();
        var realm = runtime.Realm;

        var fired = false;
        runtime.WithActiveVm(() =>
        {
            realm.Microtasks.Enqueue(() => fired = true);
        });

        fired.Should().BeTrue("WithActiveVm must drain queued microtasks before returning");
    }

    [TestMethod]
    public void WithActiveVm_preserves_existing_ActiveVm_inside_script_frame()
    {
        var runtime = new JsRuntime();
        var realm = runtime.Realm;

        JsVm? activeDuringRun = null;
        JsVm? activeInsideHelper = null;

        runtime.RegisterGlobal("probe", _ =>
        {
            activeDuringRun = realm.ActiveVm;
            runtime.WithActiveVm(() =>
            {
                activeInsideHelper = realm.ActiveVm;
            });
            return JsValue.Undefined;
        });

        var chunk = JsCompiler.Compile(new JsParser("probe();").ParseProgram());
        new JsVm(runtime).Run(chunk);

        activeDuringRun.Should().NotBeNull();
        activeInsideHelper.Should().BeSameAs(activeDuringRun,
            "inside an existing Run frame, WithActiveVm should reuse the active VM rather than allocating a new one");
    }
}
