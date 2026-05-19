namespace Tessera.Js.Runtime;

/// <summary>ECMA-262 §25.1 ArrayBuffer object with a managed byte[] [[ArrayBufferData]].</summary>
public sealed class JsArrayBuffer : JsObject
{
    public JsArrayBuffer(JsObject? prototype, int byteLength) : base(prototype)
    {
        if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
        Bytes = new byte[byteLength];
        DefineOwnProperty("byteLength",
            PropertyDescriptor.Data(JsValue.Number(byteLength), writable: false, enumerable: false, configurable: true));
    }

    private JsArrayBuffer(JsObject? prototype, byte[] bytes) : base(prototype)
    {
        Bytes = bytes;
        DefineOwnProperty("byteLength",
            PropertyDescriptor.Data(JsValue.Number(bytes.Length), writable: false, enumerable: false, configurable: true));
    }

    public byte[] Bytes { get; }
    public int ByteLength => Bytes.Length;

    public JsArrayBuffer Slice(JsObject? prototype, int begin, int end)
    {
        var len = Math.Max(end - begin, 0);
        var copy = new byte[len];
        Array.Copy(Bytes, begin, copy, 0, len);
        return new JsArrayBuffer(prototype, copy);
    }

    public override string ToString() => "[object ArrayBuffer]";
}
