using Starling.Js.Bytecode;

namespace Starling.Js.Runtime;

/// <summary>
/// User-defined JS function. Wraps a compiled <see cref="Chunk"/> plus the
/// declared parameter count. Called from the VM via the regular
/// <see cref="Opcode.Call"/> opcode — the dispatcher inspects the callee
/// object and pushes a new frame for <see cref="JsFunction"/>, vs. invoking
/// the native body for <see cref="JsNativeFunction"/>.
/// </summary>
/// <remarks>
/// <para>
/// gap:closure-write-back (wp:M3-04c2): closures use <em>live binding</em>
/// semantics. At <see cref="Opcode.MakeClosure"/> time, the parent frame
/// pushes references to the shared <see cref="Cell"/> objects that hold
/// each captured binding's value; the VM constructs a fresh
/// <see cref="JsFunction"/> whose <see cref="Upvalues"/> array stores
/// those cell references (wrapped as <c>JsValue.Object(cell)</c>). Reads
/// and writes from both the outer and the inner function go through the
/// same cell, so writes propagate as the spec requires.
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
    /// <summary>Captured-binding cells, in the order assigned by the
    /// compiler. Each entry is a <c>JsValue.Object(cell)</c> where the
    /// <see cref="Cell"/> aliases the same storage the owning scope reads
    /// and writes. Empty for plain (non-capturing) functions.</summary>
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

    /// <summary>Mangled private-name keys for this class's <em>instance</em>
    /// private methods/accessors. These live on the prototype object (shared),
    /// but each instance must still carry the brand for them in its own
    /// [[PrivateElements]] set so a wrong receiver throws a TypeError. The
    /// brands are installed onto the new instance when its field initializers
    /// run (post-<c>super()</c> for a derived class) — see
    /// <see cref="Bytecode.Opcode.RunFieldInits"/>.</summary>
    public IReadOnlyList<string>? InstancePrivateBrands { get; set; }

    /// <summary>B1b-2c — function kind. Normal functions run synchronously
    /// and return their body's value. Async functions return a Promise;
    /// generator functions return a Generator object; async generators
    /// return an Async-Iterator yielding Promises.</summary>
    public JsFunctionKind Kind { get; set; } = JsFunctionKind.Normal;

    /// <summary>§14.11 / §10.2.1 — snapshot of the object Environment Records
    /// (with-objects) active when this function instance was created. Non-null
    /// only when the function's body was compiled lexically inside one or more
    /// <c>with</c> statements (<see cref="Chunk.CapturesWith"/>). The VM seeds
    /// the callee frame's with-stack from this so free-identifier references in
    /// the body still consult the enclosing with-objects, per the closure's
    /// captured environment.</summary>
    public IReadOnlyList<JsObject>? CapturedWith { get; set; }

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
            Kind = template.Kind,
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
/// slots become non-enumerable per spec.
/// <para>
/// wp:M3-04f — when <see cref="ComputedKey"/> is non-null the field had a
/// computed key (<c>[expr] = ...</c>) whose coerced key was resolved once at
/// class-definition time. The <see cref="Thunk"/> then merely evaluates the
/// initializer (with <c>this</c> bound to the instance) and returns the
/// value; the runtime defines the own property under
/// <see cref="ComputedKey"/>. <see cref="FieldKey"/> is unused in that case.
/// </para></summary>
public sealed record InstanceFieldInit(
    string FieldKey, bool IsPrivate, JsFunction Thunk, JsPropertyKey? ComputedKey = null);

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

/// <summary>B1b-2c — function kind, distinguishes how the body executes.
/// Set by the compiler from the AST flags and consulted by the VM when a
/// function is invoked.</summary>
public enum JsFunctionKind : byte
{
    /// <summary>Plain function — body runs synchronously.</summary>
    Normal = 0,
    /// <summary>Async function — invocation returns a Promise; the body runs
    /// on a worker thread and suspends at <c>await</c> via the VM's
    /// <see cref="Starling.Js.Bytecode.Opcode.Suspend"/> opcode.</summary>
    Async = 1,
    /// <summary>Generator function — invocation returns a Generator
    /// (iterator) object; body suspends at each <c>yield</c>.</summary>
    Generator = 2,
    /// <summary>Async generator — invocation returns an Async-Iterator;
    /// body suspends at both <c>yield</c> and <c>await</c>.</summary>
    AsyncGenerator = 3,
}
