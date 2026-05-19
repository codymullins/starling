using FluentAssertions;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// Pins the B0 runtime foundations: prototype-chain Get, property descriptors,
/// JsRealm slot wiring, AbstractOperations.Call/Construct dispatch.
/// </summary>
[TestClass]
public class JsRealmAndProtoTests
{
    [TestMethod]
    public void JsObject_Get_walks_the_prototype_chain()
    {
        var proto = new JsObject();
        proto.Set("inherited", JsValue.Number(7));
        var obj = new JsObject(proto);

        obj.Get("inherited").AsNumber.Should().Be(7);
        obj.Has("inherited").Should().BeTrue();
        obj.HasOwn("inherited").Should().BeFalse();
    }

    [TestMethod]
    public void JsObject_Set_creates_own_property_even_when_proto_has_one()
    {
        var proto = new JsObject();
        proto.Set("k", JsValue.String("from-proto"));
        var obj = new JsObject(proto);

        obj.Set("k", JsValue.String("from-own"));

        obj.Get("k").AsString.Should().Be("from-own");
        proto.Get("k").AsString.Should().Be("from-proto");
        obj.HasOwn("k").Should().BeTrue();
    }

    [TestMethod]
    public void Non_writable_descriptor_rejects_Set()
    {
        var obj = new JsObject();
        obj.DefineOwnProperty("readonly",
            PropertyDescriptor.Data(JsValue.Number(1), writable: false, enumerable: true, configurable: true));

        obj.Set("readonly", JsValue.Number(2));

        obj.Get("readonly").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Delete_rejects_non_configurable_property()
    {
        var obj = new JsObject();
        obj.DefineOwnProperty("locked",
            PropertyDescriptor.Data(JsValue.Number(1), writable: true, enumerable: true, configurable: false));

        obj.Delete("locked").Should().BeFalse();
        obj.HasOwn("locked").Should().BeTrue();
    }

    [TestMethod]
    public void SetPrototypeOf_rejects_cycle()
    {
        var a = new JsObject();
        var b = new JsObject(a);

        a.SetPrototypeOf(b).Should().BeFalse(); // would form a -> b -> a cycle.
    }

    [TestMethod]
    public void EnumerableKeys_filters_non_enumerable_slots()
    {
        var obj = new JsObject();
        obj.DefineOwnProperty("hidden",
            PropertyDescriptor.Data(JsValue.Null, writable: true, enumerable: false, configurable: true));
        obj.Set("visible", JsValue.Null);

        obj.EnumerableKeys().Should().BeEquivalentTo(new[] { "visible" });
    }

    // ------------------------------------------------------------------ Realm

    [TestMethod]
    public void Realm_pre_wires_prototype_chains()
    {
        var realm = new JsRealm();

        realm.FunctionPrototype.Prototype.Should().BeSameAs(realm.ObjectPrototype);
        realm.ArrayPrototype.Prototype.Should().BeSameAs(realm.ObjectPrototype);
        realm.TypeErrorPrototype.Prototype.Should().BeSameAs(realm.ErrorPrototype);
        realm.ErrorPrototype.Prototype.Should().BeSameAs(realm.ObjectPrototype);
    }

    [TestMethod]
    public void NewError_attaches_message_slot()
    {
        var realm = new JsRealm();
        var err = realm.NewTypeError("boom").AsObject;

        err.Prototype.Should().BeSameAs(realm.TypeErrorPrototype);
        err.Get("message").AsString.Should().Be("boom");
    }

    // ------------------------------------------------------------ AbstractOps

    [TestMethod]
    public void AbstractOperations_IsCallable_recognizes_natives_and_bound()
    {
        var realm = new JsRealm();
        var nat = new JsNativeFunction("f", (_, _) => JsValue.Undefined);
        AbstractOperations.IsCallable(JsValue.Object(nat)).Should().BeTrue();

        var bound = new JsBoundFunction(nat, JsValue.Undefined, Array.Empty<JsValue>(), realm.FunctionPrototype);
        AbstractOperations.IsCallable(JsValue.Object(bound)).Should().BeTrue();

        AbstractOperations.IsCallable(JsValue.Number(1)).Should().BeFalse();
        AbstractOperations.IsCallable(JsValue.Object(new JsObject())).Should().BeFalse();
    }

    [TestMethod]
    public void AbstractOperations_Call_dispatches_to_native()
    {
        JsValue captured = JsValue.Undefined;
        var nat = new JsNativeFunction("capture", (thisV, args) =>
        {
            captured = args[0];
            return JsValue.Number(thisV.IsNumber ? thisV.AsNumber : -1);
        });

        var result = AbstractOperations.Call(vm: null, JsValue.Object(nat), JsValue.Number(99), [JsValue.String("hi")]);

        result.AsNumber.Should().Be(99);
        captured.AsString.Should().Be("hi");
    }

    [TestMethod]
    public void AbstractOperations_Construct_on_native_invokes_with_new_target_as_this()
    {
        JsValue thisSeen = JsValue.Undefined;
        var nat = new JsNativeFunction("Ctor", (thisV, _) =>
        {
            thisSeen = thisV;
            return JsValue.Undefined;
        });

        AbstractOperations.Construct(vm: null, JsValue.Object(nat), Array.Empty<JsValue>());

        thisSeen.IsObject.Should().BeTrue();
        thisSeen.AsObject.Should().BeSameAs(nat);
    }

    [TestMethod]
    public void Non_constructable_native_rejected_by_Construct()
    {
        var nat = new JsNativeFunction("notCtor", _ => JsValue.Undefined); // legacy ctor → isConstructor=false
        var act = () => AbstractOperations.Construct(vm: null, JsValue.Object(nat), Array.Empty<JsValue>());
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void SameValue_treats_NaN_as_equal_and_distinguishes_signed_zero()
    {
        AbstractOperations.SameValue(JsValue.Number(double.NaN), JsValue.Number(double.NaN)).Should().BeTrue();
        AbstractOperations.SameValue(JsValue.Number(0), JsValue.Number(-0.0)).Should().BeFalse();
        AbstractOperations.SameValueZero(JsValue.Number(0), JsValue.Number(-0.0)).Should().BeTrue();
    }
}
