using System.Buffers.Binary;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>ECMA-262 §25.3 DataView Objects.</summary>
public static class DataViewCtor
{
    private const double MaxSafeInteger = 9007199254740991d;

    private sealed class JsDataView : JsObject
    {
        public JsDataView(JsObject? prototype, JsArrayBuffer buffer, int byteOffset, int byteLength) : base(prototype)
        {
            Buffer = buffer;
            ByteOffset = byteOffset;
            ByteLength = byteLength;
            DefineOwnProperty("__DataView", PropertyDescriptor.Data(JsValue.True, false, false, false));
            DefineOwnProperty("buffer", PropertyDescriptor.Data(JsValue.Object(buffer), false, false, true));
            DefineOwnProperty("byteOffset", PropertyDescriptor.Data(JsValue.Number(byteOffset), false, false, true));
            DefineOwnProperty("byteLength", PropertyDescriptor.Data(JsValue.Number(byteLength), false, false, true));
        }

        public JsArrayBuffer Buffer { get; }
        public int ByteOffset { get; }
        public int ByteLength { get; }
    }

    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var proto = realm.DataViewPrototype;

        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction("DataView", (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
                throw new JsThrow(realm.NewTypeError("DataView constructor requires 'new'"));
            if (args.Length == 0 || !args[0].IsObject || args[0].AsObject is not JsArrayBuffer buffer)
                throw new JsThrow(realm.NewTypeError("DataView buffer must be an ArrayBuffer"));
            var offset = args.Length > 1 ? ArrayBufferCtor.ToIndex(realm, args[1]) : 0;
            if (offset > buffer.ByteLength) throw new JsThrow(realm.NewRangeError("DataView byteOffset out of range"));
            var length = args.Length > 2 && !args[2].IsUndefined
                ? ArrayBufferCtor.ToIndex(realm, args[2])
                : buffer.ByteLength - offset;
            if (offset + length > buffer.ByteLength) throw new JsThrow(realm.NewRangeError("DataView byteLength out of range"));
            // §25.3.2.1 step 10: OrdinaryCreateFromConstructor — prototype from new.target.
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(new JsDataView(instProto, buffer, offset, length));
        }, isConstructor: true);
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        ArrayBufferCtor.DefineData(ctor, "prototype", JsValue.Object(proto), false, false, false);
        ArrayBufferCtor.DefineData(ctor, "name", JsValue.String("DataView"), false, false, true);
        ArrayBufferCtor.DefineData(ctor, "length", JsValue.Number(1), false, false, true);
        ArrayBufferCtor.DefineData(proto, "constructor", JsValue.Object(ctor), true, false, true);
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("DataView"), writable: false, enumerable: false, configurable: true));

        DefineGet(realm, proto, "getInt8", 1, 1, (s, le) => JsValue.Number(unchecked((sbyte)s[0])));
        DefineGet(realm, proto, "getUint8", 1, 1, (s, le) => JsValue.Number(s[0]));
        DefineGet(realm, proto, "getInt16", 2, 2, (s, le) => JsValue.Number(le ? BinaryPrimitives.ReadInt16LittleEndian(s) : BinaryPrimitives.ReadInt16BigEndian(s)));
        DefineGet(realm, proto, "getUint16", 2, 2, (s, le) => JsValue.Number(le ? BinaryPrimitives.ReadUInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16BigEndian(s)));
        DefineGet(realm, proto, "getInt32", 4, 2, (s, le) => JsValue.Number(le ? BinaryPrimitives.ReadInt32LittleEndian(s) : BinaryPrimitives.ReadInt32BigEndian(s)));
        DefineGet(realm, proto, "getUint32", 4, 2, (s, le) => JsValue.Number(le ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s)));
        DefineGet(realm, proto, "getFloat32", 4, 2, (s, le) => JsValue.Number(le ? BinaryPrimitives.ReadSingleLittleEndian(s) : BinaryPrimitives.ReadSingleBigEndian(s)));
        DefineGet(realm, proto, "getFloat64", 8, 2, (s, le) => JsValue.Number(le ? BinaryPrimitives.ReadDoubleLittleEndian(s) : BinaryPrimitives.ReadDoubleBigEndian(s)));
        DefineGet(realm, proto, "getBigInt64", 8, 2, (s, le) => ReadBigInt64(realm, s, le));
        DefineGet(realm, proto, "getBigUint64", 8, 2, (s, le) => ReadBigUint64(realm, s, le));

        DefineSet(realm, proto, "setInt8", 1, 2, (s, v, le) => s[0] = unchecked((byte)(sbyte)ToInt32(ArrayBufferCtor.Number(v))));
        DefineSet(realm, proto, "setUint8", 1, 2, (s, v, le) => s[0] = unchecked((byte)ToUint32(ArrayBufferCtor.Number(v))));
        DefineSet(realm, proto, "setInt16", 2, 3, (s, v, le) => Write(le, s, unchecked((short)ToInt32(ArrayBufferCtor.Number(v)))));
        DefineSet(realm, proto, "setUint16", 2, 3, (s, v, le) => Write(le, s, unchecked((ushort)ToUint32(ArrayBufferCtor.Number(v)))));
        DefineSet(realm, proto, "setInt32", 4, 3, (s, v, le) => Write(le, s, ToInt32(ArrayBufferCtor.Number(v))));
        DefineSet(realm, proto, "setUint32", 4, 3, (s, v, le) => Write(le, s, ToUint32(ArrayBufferCtor.Number(v))));
        DefineSet(realm, proto, "setFloat32", 4, 3, (s, v, le) => { if (le) BinaryPrimitives.WriteSingleLittleEndian(s, (float)ArrayBufferCtor.Number(v)); else BinaryPrimitives.WriteSingleBigEndian(s, (float)ArrayBufferCtor.Number(v)); });
        DefineSet(realm, proto, "setFloat64", 8, 3, (s, v, le) => { if (le) BinaryPrimitives.WriteDoubleLittleEndian(s, ArrayBufferCtor.Number(v)); else BinaryPrimitives.WriteDoubleBigEndian(s, ArrayBufferCtor.Number(v)); });
        DefineSet(realm, proto, "setBigInt64", 8, 3, (s, v, le) => WriteBigInt64(realm, s, ArrayBufferCtor.Number(v), le));
        DefineSet(realm, proto, "setBigUint64", 8, 3, (s, v, le) => WriteBigUint64(realm, s, ArrayBufferCtor.Number(v), le));

        realm.GlobalObject.DefineOwnProperty("DataView", PropertyDescriptor.Data(JsValue.Object(ctor), true, false, true));
    }

    private static void DefineGet(JsRealm realm, JsObject proto, string name, int size, int length, Func<ReadOnlySpan<byte>, bool, JsValue> read)
        => ArrayBufferCtor.DefineMethod(proto, name, (thisV, args) =>
        {
            var view = ThisView(realm, thisV);
            var offset = args.Length > 0 ? ArrayBufferCtor.ToIndex(realm, args[0]) : 0;
            var le = args.Length > 1 && JsValue.ToBoolean(args[1]);
            var span = CheckedSpan(realm, view, offset, size);
            return read(span, le);
        }, length);

    private static void DefineSet(JsRealm realm, JsObject proto, string name, int size, int length, Action<Span<byte>, JsValue, bool> write)
        => ArrayBufferCtor.DefineMethod(proto, name, (thisV, args) =>
        {
            var view = ThisView(realm, thisV);
            var offset = args.Length > 0 ? ArrayBufferCtor.ToIndex(realm, args[0]) : 0;
            var value = args.Length > 1 ? args[1] : JsValue.Undefined;
            var le = args.Length > 2 && JsValue.ToBoolean(args[2]);
            var span = CheckedSpan(realm, view, offset, size);
            write(span, value, le);
            return JsValue.Undefined;
        }, length);

    private static JsDataView ThisView(JsRealm realm, JsValue thisV)
        => thisV.IsObject && thisV.AsObject is JsDataView view
            ? view
            : throw new JsThrow(realm.NewTypeError("DataView method called on incompatible receiver"));

    private static Span<byte> CheckedSpan(JsRealm realm, JsDataView view, int offset, int size)
    {
        if (offset < 0 || offset + size > view.ByteLength)
            throw new JsThrow(realm.NewRangeError("DataView byteOffset out of range"));
        return view.Buffer.Bytes.AsSpan(view.ByteOffset + offset, size);
    }

    private static JsValue ReadBigInt64(JsRealm realm, ReadOnlySpan<byte> s, bool le)
    {
        var v = le ? BinaryPrimitives.ReadInt64LittleEndian(s) : BinaryPrimitives.ReadInt64BigEndian(s);
        if (Math.Abs((double)v) > MaxSafeInteger) throw BigIntTypeError(realm);
        return JsValue.Number(v);
    }

    private static JsValue ReadBigUint64(JsRealm realm, ReadOnlySpan<byte> s, bool le)
    {
        var v = le ? BinaryPrimitives.ReadUInt64LittleEndian(s) : BinaryPrimitives.ReadUInt64BigEndian(s);
        if (v > (ulong)MaxSafeInteger) throw BigIntTypeError(realm);
        return JsValue.Number(v);
    }

    private static void WriteBigInt64(JsRealm realm, Span<byte> s, double n, bool le)
    {
        if (!IsSafeInteger(n)) throw BigIntTypeError(realm);
        if (le) BinaryPrimitives.WriteInt64LittleEndian(s, (long)Math.Truncate(n));
        else BinaryPrimitives.WriteInt64BigEndian(s, (long)Math.Truncate(n));
    }

    private static void WriteBigUint64(JsRealm realm, Span<byte> s, double n, bool le)
    {
        if (!IsSafeInteger(n) || n < 0) throw BigIntTypeError(realm);
        if (le) BinaryPrimitives.WriteUInt64LittleEndian(s, (ulong)Math.Truncate(n));
        else BinaryPrimitives.WriteUInt64BigEndian(s, (ulong)Math.Truncate(n));
    }

    private static JsThrow BigIntTypeError(JsRealm realm)
        => new(realm.NewTypeError("B4-3 BigInt: DataView BigInt values outside Number safe-integer range are not implemented"));

    private static bool IsSafeInteger(double n)
        => double.IsFinite(n) && n == Math.Truncate(n) && Math.Abs(n) <= MaxSafeInteger;

    // ECMA-262 §7.1.6 / §7.1.7 conversions used by §25.3 setters.
    private static int ToInt32(double n) => unchecked((int)ToUint32(n));

    private static uint ToUint32(double n)
    {
        if (double.IsNaN(n) || double.IsInfinity(n) || n == 0) return 0;
        var t = Math.Truncate(n);
        var mod = t - Math.Floor(t / 4294967296d) * 4294967296d;
        return (uint)mod;
    }

    private static void Write(bool le, Span<byte> s, short v) { if (le) BinaryPrimitives.WriteInt16LittleEndian(s, v); else BinaryPrimitives.WriteInt16BigEndian(s, v); }
    private static void Write(bool le, Span<byte> s, ushort v) { if (le) BinaryPrimitives.WriteUInt16LittleEndian(s, v); else BinaryPrimitives.WriteUInt16BigEndian(s, v); }
    private static void Write(bool le, Span<byte> s, int v) { if (le) BinaryPrimitives.WriteInt32LittleEndian(s, v); else BinaryPrimitives.WriteInt32BigEndian(s, v); }
    private static void Write(bool le, Span<byte> s, uint v) { if (le) BinaryPrimitives.WriteUInt32LittleEndian(s, v); else BinaryPrimitives.WriteUInt32BigEndian(s, v); }
}
