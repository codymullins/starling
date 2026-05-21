using Starling.Js.Runtime;

namespace Starling.Js.Bytecode;

/// <summary>
/// B1b-2a — compile-time description of a class. Stored as a constant-pool
/// entry and consumed at runtime by <see cref="Opcode.BuildClass"/>, which
/// allocates the constructor, installs methods/fields, and wires the
/// <c>[[Prototype]]</c> chain.
/// </summary>
/// <remarks>
/// <para>
/// Method bodies, field initializers, and static blocks are all compiled as
/// <see cref="JsFunction"/> templates. At <c>BuildClass</c> time the VM
/// wraps each into a runtime instance with the constructor's
/// <see cref="JsFunction.HomeObject"/> slot pointing at the appropriate
/// owner (prototype for instance members, constructor for static members).
/// </para>
/// <para>
/// Private slot lookups use a class-unique mangled name produced by
/// <see cref="MangledPrivateName"/> — instances store their private slots
/// as string-keyed own properties under that mangled key, so the existing
/// <see cref="JsObject"/> property machinery is reused with no extra
/// allocations.
/// </para>
/// </remarks>
public sealed class ClassTemplate
{
    /// <summary>Source-level name (or "" for anonymous class expressions).</summary>
    public string Name { get; }

    /// <summary>Constructor function template — already stamped with the
    /// appropriate <see cref="ClassConstructorKind"/>. May be a synthesized
    /// default for classes that omit the <c>constructor</c> member.</summary>
    public JsFunction ConstructorTemplate { get; }

    /// <summary>True if the class has an <c>extends</c> clause. The VM
    /// expects a base-class value on top of the stack before
    /// <see cref="Opcode.BuildClass"/>.</summary>
    public bool HasExtends { get; }

    public IReadOnlyList<MethodEntry> Methods { get; }
    public IReadOnlyList<FieldEntry> Fields { get; }
    public IReadOnlyList<StaticBlockEntry> StaticBlocks { get; }
    /// <summary>Number of upvalue snapshots the parent frame must push for
    /// the constructor itself (since the constructor is built first).</summary>
    public int ConstructorUpvalueCount { get; }

    /// <summary>Class-unique id stamped into private-name keys so two
    /// classes that declare <c>#x</c> can't collide.</summary>
    public int ClassId { get; }

    public ClassTemplate(
        string name,
        JsFunction constructorTemplate,
        int constructorUpvalueCount,
        bool hasExtends,
        IReadOnlyList<MethodEntry> methods,
        IReadOnlyList<FieldEntry> fields,
        IReadOnlyList<StaticBlockEntry> staticBlocks,
        int classId)
    {
        ConstructorUpvalueCount = constructorUpvalueCount;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ConstructorTemplate = constructorTemplate ?? throw new ArgumentNullException(nameof(constructorTemplate));
        HasExtends = hasExtends;
        Methods = methods ?? throw new ArgumentNullException(nameof(methods));
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        StaticBlocks = staticBlocks ?? throw new ArgumentNullException(nameof(staticBlocks));
        ClassId = classId;
    }

    private static int s_nextClassId;
    public static int NextClassId() => System.Threading.Interlocked.Increment(ref s_nextClassId);

    /// <summary>Produce the mangled own-property key used to store a
    /// private slot. The "@@PC" prefix is reserved ("@" is not a valid
    /// JS IdentifierName start) so the name can never collide with a
    /// user-visible string key.</summary>
    public static string MangledPrivateName(int classId, string privateName)
        => "@@PC" + classId.ToString(System.Globalization.CultureInfo.InvariantCulture) + privateName;
}

/// <summary>One method definition compiled for a class. Computed-key
/// methods (wp:M3-04f) leave both <see cref="StaticKey"/> and
/// <see cref="MangledPrivateKey"/> null, set <see cref="IsComputed"/>, and
/// rely on the parent frame pushing the already-coerced property key onto
/// the stack (below this entry's upvalues) for <see cref="Opcode.BuildClass"/>
/// to consume.</summary>
public sealed record MethodEntry(
    string? StaticKey,
    string? MangledPrivateKey,
    ClassMethodKind Kind,
    bool IsStatic,
    JsFunction Template,
    int UpvalueCount,
    bool IsComputed = false);

public enum ClassMethodKind
{
    Method,
    Get,
    Set,
}

/// <summary>One field declaration compiled for a class. Field initializers
/// are wrapped in zero-arg <see cref="JsFunction"/> bodies so they execute
/// with <c>this</c> = instance (or constructor, for statics).</summary>
public sealed record FieldEntry(
    string? StaticKey,           // null for private or computed
    string? MangledPrivateKey,   // non-null for private fields
    bool IsStatic,
    JsFunction? InitializerTemplate,  // null when the field has no initializer
    int UpvalueCount,
    // wp:M3-04f — computed-key field. The already-coerced property key is
    // pushed onto the stack (below this entry's upvalues) for BuildClass to
    // consume. For computed fields the InitializerTemplate, when present,
    // simply evaluates the initializer expression (with `this` bound) and
    // *returns* the value rather than self-storing under a baked key.
    bool IsComputed = false);

/// <summary>One <c>static { ... }</c> initialization block. Compiled as a
/// zero-arg <see cref="JsFunction"/> whose <c>this</c> is the constructor.</summary>
public sealed record StaticBlockEntry(JsFunction Template, int UpvalueCount);
