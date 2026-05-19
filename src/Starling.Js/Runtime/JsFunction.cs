using Tessera.Js.Bytecode;

namespace Tessera.Js.Runtime;

/// <summary>
/// User-defined JS function. Wraps a compiled <see cref="Chunk"/> plus the
/// declared parameter count. Called from the VM via the regular
/// <see cref="Opcode.Call"/> opcode — the dispatcher inspects the callee
/// object and pushes a new frame for <see cref="JsFunction"/>, vs. invoking
/// the native body for <see cref="JsNativeFunction"/>.
/// </summary>
/// <remarks>
/// <para>
/// Closures (wp:M3-04c) use <em>snapshot semantics</em>: at
/// <see cref="Opcode.MakeClosure"/> time, the parent frame pushes the
/// current values of each captured variable, and the VM constructs a
/// fresh <see cref="JsFunction"/> whose <see cref="Upvalues"/> array
/// holds those snapshots. Mutation through an upvalue (i.e. the inner
/// function reassigning a captured name and the parent observing it)
/// is deferred to wp:M3-04c2 with Cell-based slots.
/// </para>
/// <para>
/// A "template" JsFunction is what the compiler stuffs into the
/// constant pool — its <see cref="Upvalues"/> is empty and it is never
/// directly callable when the function has free captures. The runtime
/// instance with bound upvalues is built by <see cref="Opcode.MakeClosure"/>.
/// </para>
/// </remarks>
public sealed class JsFunction : JsObject
{
    public string Name { get; }
    public Chunk Body { get; }
    public int ArityDeclared { get; }
    /// <summary>Snapshotted captured values, in the order assigned by the
    /// compiler. Empty for plain (non-capturing) functions.</summary>
    public IReadOnlyList<JsValue> Upvalues { get; }

    /// <summary>B1b-2a: the prototype object on which this function lives
    /// when it is a class method (or the constructor for static methods).
    /// <c>super.x</c> resolves to <c>HomeObject.[[Prototype]][x]</c>.
    /// Null for plain functions and template instances.</summary>
    public JsObject? HomeObject { get; set; }

    /// <summary>B1b-2a: <c>ClassConstructorKind</c>. <see cref="ClassConstructorKind.None"/>
    /// means a plain function. <see cref="ClassConstructorKind.Base"/> is a
    /// class constructor without <c>extends</c>; <see cref="ClassConstructorKind.Derived"/>
    /// requires <c>super(...)</c> before any <c>this</c> access.</summary>
    public ClassConstructorKind ConstructorKind { get; set; } = ClassConstructorKind.None;

    /// <summary>B1b-2a: instance-field initializer thunks. Each entry is a
    /// pre-instantiated zero-arg <see cref="JsFunction"/> invoked with
    /// <c>this</c> = the new instance after the (possibly synthesized)
    /// <c>super(...)</c> returns, in declaration order. The
    /// <see cref="InstanceFieldInit.FieldKey"/> selects the slot to write —
    /// null entries indicate computed keys (not yet supported by
    /// B1b-2a).</summary>
    public IReadOnlyList<InstanceFieldInit>? InstanceFieldInitializers { get; set; }

    public JsFunction(string name, Chunk body, int arityDeclared)
        : this(name, body, arityDeclared, Array.Empty<JsValue>())
    {
    }

    public JsFunction(string name, Chunk body, int arityDeclared, IReadOnlyList<JsValue> upvalues)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        ArityDeclared = arityDeclared;
        Upvalues = upvalues ?? throw new ArgumentNullException(nameof(upvalues));
    }

    /// <summary>B2-2: build a runtime-ready function instance from a compile-time
    /// template. Wires <c>[[Prototype]] = realm.FunctionPrototype</c> so the
    /// new function inherits <c>call</c>/<c>apply</c>/<c>bind</c>, then stamps
    /// the standard own properties:
    /// <list type="bullet">
    ///   <item><c>name</c> — declared name (or "" for anonymous).</item>
    ///   <item><c>length</c> — declared positional arity.</item>
    ///   <item><c>prototype</c> — fresh <c>{ constructor: thisFn }</c> object
    ///   inheriting from <see cref="JsRealm.ObjectPrototype"/>, used by
    ///   <c>new</c> as the new-target prototype per §10.2.4.</item>
    /// </list>
    /// The template object in the constant pool is never itself returned to
    /// JS — every <c>LoadFunction</c>/<c>MakeClosure</c> dispatch produces a
    /// fresh instance via this helper.</summary>
    public static JsFunction CreateInstance(
        JsRealm realm, JsFunction template, IReadOnlyList<JsValue> upvalues)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(template);
        var fn = new JsFunction(template.Name, template.Body, template.ArityDeclared, upvalues)
        {
            ConstructorKind = template.ConstructorKind,
            // HomeObject is copied through to the instance so per-call closures
            // still resolve super correctly. For class methods/ctors compiled
            // via DefineClass the template carries the slot.
            HomeObject = template.HomeObject,
        };
        fn.SetPrototypeOf(realm.FunctionPrototype);

        // §10.2.4 — the function's own `prototype` slot holds the object that
        // `new f()` uses as the new-target prototype. Spec descriptor is
        // writable=true, enumerable=false, configurable=false.
        var protoObj = new JsObject(realm.ObjectPrototype);
        protoObj.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(fn), writable: true, enumerable: false, configurable: true));
        fn.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(protoObj), writable: true, enumerable: false, configurable: false));

        // `name` and `length` are non-enumerable, non-writable, configurable per §17.
        fn.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String(template.Name), writable: false, enumerable: false, configurable: true));
        fn.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(template.ArityDeclared), writable: false, enumerable: false, configurable: true));
        return fn;
    }

    public override string ToString()
        => $"function {Name}({ArityDeclared}) {{ [bytecode] }}";
}

/// <summary>One instance-field initializer attached to a class constructor.
/// <see cref="FieldKey"/> is the property name to write; for a private
/// field it's the mangled name. <see cref="IsPrivate"/> selects between
/// <c>DefineOwnProperty</c> and the public <c>Set</c> path so private
/// slots become non-enumerable per spec.</summary>
public sealed record InstanceFieldInit(string FieldKey, bool IsPrivate, JsFunction Thunk);

/// <summary>
/// ES2024 §10.2.1 [[ConstructorKind]] internal slot. Distinguishes between
/// plain functions, base-class constructors, and derived-class constructors —
/// the last requires <c>super(...)</c> to bind <c>this</c> before any use.
/// </summary>
public enum ClassConstructorKind
{
    /// <summary>Plain function (non-class).</summary>
    None,
    /// <summary>Base-class constructor — pre-allocates <c>this</c> per the
    /// ordinary [[Construct]] semantics.</summary>
    Base,
    /// <summary>Derived-class constructor — <c>this</c> is uninitialized until
    /// <c>super(...)</c> completes.</summary>
    Derived,
}
