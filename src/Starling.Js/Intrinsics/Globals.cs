using System.Text;
using Tessera.Js.Runtime;

namespace Tessera.Js.Intrinsics;

/// <summary>§19.2 global functions used by browser scripts.</summary>
public static class Globals
{
    private const string UriReserved = ";/?:@&=+$,#";
    private const string UriUnescaped = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.!~*'()";

    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        DefineGlobal(realm, "parseInt", (_, args) => NumberCtor.ParseInt(args), 2);
        DefineGlobal(realm, "parseFloat", (_, args) => NumberCtor.ParseFloat(args), 1);
        DefineGlobal(realm, "isNaN", (_, args) => JsValue.Boolean(double.IsNaN(NumberCtor.ToNumber(args.Length > 0 ? args[0] : JsValue.Undefined))), 1);
        DefineGlobal(realm, "isFinite", (_, args) => JsValue.Boolean(double.IsFinite(NumberCtor.ToNumber(args.Length > 0 ? args[0] : JsValue.Undefined))), 1);
        DefineGlobal(realm, "encodeURI", (_, args) => JsValue.String(Encode(args.Length > 0 ? args[0] : JsValue.Undefined, UriReserved, realm)), 1);
        DefineGlobal(realm, "encodeURIComponent", (_, args) => JsValue.String(Encode(args.Length > 0 ? args[0] : JsValue.Undefined, string.Empty, realm)), 1);
        DefineGlobal(realm, "decodeURI", (_, args) => JsValue.String(Decode(args.Length > 0 ? args[0] : JsValue.Undefined, preserveReserved: true, realm)), 1);
        DefineGlobal(realm, "decodeURIComponent", (_, args) => JsValue.String(Decode(args.Length > 0 ? args[0] : JsValue.Undefined, preserveReserved: false, realm)), 1);
    }

    /// <summary>§19.2.6.4 Encode — percent-encode UTF-8 bytes, preserving encodeURI's reserved set.</summary>
    private static string Encode(JsValue value, string extraUnescaped, JsRealm realm)
    {
        var s = JsValue.ToStringValue(value);
        var sb = new StringBuilder();
        var utf8 = new UTF8Encoding(false, true);
        foreach (var rune in s.EnumerateRunes())
        {
            var text = rune.ToString();
            if (text.Length == 1 && (UriUnescaped.IndexOf(text[0], StringComparison.Ordinal) >= 0 || extraUnescaped.IndexOf(text[0], StringComparison.Ordinal) >= 0))
            {
                sb.Append(text[0]);
                continue;
            }
            byte[] bytes;
            try { bytes = utf8.GetBytes(text); }
            catch (EncoderFallbackException) { throw new JsThrow(realm.NewUriError("URI malformed")); }
            foreach (var b in bytes)
            {
                sb.Append('%');
                sb.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return sb.ToString();
    }

    /// <summary>§19.2.6.5 Decode — validate percent triplets and UTF-8, preserving reserved escapes for decodeURI.</summary>
    private static string Decode(JsValue value, bool preserveReserved, JsRealm realm)
    {
        var s = JsValue.ToStringValue(value);
        var sb = new StringBuilder();
        var utf8 = new UTF8Encoding(false, true);
        for (var i = 0; i < s.Length;)
        {
            if (s[i] != '%') { sb.Append(s[i++]); continue; }
            var start = i;
            var bytes = new List<byte>();
            while (i < s.Length && s[i] == '%')
            {
                if (i + 2 >= s.Length || !IsHex(s[i + 1]) || !IsHex(s[i + 2]))
                    throw new JsThrow(realm.NewUriError("URI malformed"));
                bytes.Add((byte)((HexValue(s[i + 1]) << 4) | HexValue(s[i + 2])));
                i += 3;
            }
            string decoded;
            try { decoded = utf8.GetString(bytes.ToArray()); }
            catch (DecoderFallbackException) { throw new JsThrow(realm.NewUriError("URI malformed")); }
            if (preserveReserved && decoded.Length == 1 && UriReserved.IndexOf(decoded[0], StringComparison.Ordinal) >= 0)
                sb.Append(s, start, i - start);
            else
                sb.Append(decoded);
        }
        return sb.ToString();
    }

    private static bool IsHex(char c) => char.IsAsciiHexDigit(c);
    private static int HexValue(char c) => c <= '9' ? c - '0' : (c <= 'F' ? c - 'A' + 10 : c - 'a' + 10);

    private static void DefineGlobal(JsRealm realm, string name, Func<JsValue, JsValue[], JsValue> body, int length)
    {
        var fn = new JsNativeFunction(name, body, isConstructor: false);
        fn.DefineOwnProperty("name", PropertyDescriptor.Data(JsValue.String(name), false, false, true));
        fn.DefineOwnProperty("length", PropertyDescriptor.Data(JsValue.Number(length), false, false, true));
        realm.GlobalObject.DefineOwnProperty(name, PropertyDescriptor.Data(JsValue.Object(fn), true, false, true));
    }
}
