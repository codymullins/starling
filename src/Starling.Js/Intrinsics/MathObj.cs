using SysMath = System.Math;
using Tessera.Js.Runtime;

namespace Tessera.Js.Intrinsics;

/// <summary>
/// §21.3 The Math Object. Installs the global <c>Math</c> namespace object on
/// the realm's global object as a non-callable, non-constructible plain object
/// with the full set of constants and static methods.
/// </summary>
/// <remarks>
/// Constants are sealed (non-writable + non-enumerable + non-configurable) per
/// spec; static methods follow the typical builtin descriptor
/// (writable + non-enumerable + configurable). Argument coercion routes through
/// <see cref="JsValue.ToNumber(JsValue)"/> — the standard §7.1.4 path — so
/// callers passing strings, booleans, etc. behave per spec. Several methods
/// deviate from the .NET <c>System.Math</c> behavior and are noted inline
/// (notably <c>round</c>, <c>sign</c>, <c>pow(NaN, 0)</c>, and variadic
/// <c>max</c>/<c>min</c>).
/// </remarks>
public static class MathObj
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var math = new JsObject(realm.ObjectPrototype);

        // ---------- Constants (sealed) ---------------------------------------
        DefineConst(math, "E", SysMath.E);
        DefineConst(math, "LN10", SysMath.Log(10));
        DefineConst(math, "LN2", SysMath.Log(2));
        DefineConst(math, "LOG10E", 1.0 / SysMath.Log(10));
        DefineConst(math, "LOG2E", 1.0 / SysMath.Log(2));
        DefineConst(math, "PI", SysMath.PI);
        DefineConst(math, "SQRT1_2", SysMath.Sqrt(0.5));
        DefineConst(math, "SQRT2", SysMath.Sqrt(2));

        // ---------- Single-argument methods ----------------------------------
        DefineUnary(math, "abs", SysMath.Abs);
        DefineUnary(math, "acos", SysMath.Acos);
        DefineUnary(math, "acosh", SysMath.Acosh);
        DefineUnary(math, "asin", SysMath.Asin);
        DefineUnary(math, "asinh", SysMath.Asinh);
        DefineUnary(math, "atan", SysMath.Atan);
        DefineUnary(math, "atanh", SysMath.Atanh);
        DefineUnary(math, "cbrt", SysMath.Cbrt);
        DefineUnary(math, "ceil", SysMath.Ceiling);
        DefineUnary(math, "cos", SysMath.Cos);
        DefineUnary(math, "cosh", SysMath.Cosh);
        DefineUnary(math, "exp", SysMath.Exp);
        DefineUnary(math, "expm1", x => SysMath.Exp(x) - 1.0); // .NET has no Expm1
        DefineUnary(math, "floor", SysMath.Floor);
        DefineUnary(math, "fround", x => (double)(float)x);
        DefineUnary(math, "log", SysMath.Log);
        DefineUnary(math, "log10", SysMath.Log10);
        DefineUnary(math, "log1p", x => SysMath.Log(1.0 + x)); // .NET has no Log1p
        DefineUnary(math, "log2", SysMath.Log2);
        DefineUnary(math, "sin", SysMath.Sin);
        DefineUnary(math, "sinh", SysMath.Sinh);
        DefineUnary(math, "sqrt", SysMath.Sqrt);
        DefineUnary(math, "tan", SysMath.Tan);
        DefineUnary(math, "tanh", SysMath.Tanh);
        DefineUnary(math, "trunc", SysMath.Truncate);

        // Math.round — JS rounds half toward +∞ (NOT to even, NOT away from zero).
        // .NET's Math.Round uses banker's rounding by default, so we hand-roll.
        DefineUnary(math, "round", JsRound);

        // Math.sign — preserves signed zero (Math.sign(-0) === -0).
        DefineUnary(math, "sign", JsSign);

        // Math.clz32 — count leading zeroes of ToUint32(x). 32 when x is 0.
        DefineUnary(math, "clz32", x => Clz32(x));

        // ---------- Two-argument methods -------------------------------------
        math.DefineOwnProperty("atan2", MethodDesc("atan2", (_, args) =>
        {
            var y = args.Length > 0 ? JsValue.ToNumber(args[0]) : double.NaN;
            var x = args.Length > 1 ? JsValue.ToNumber(args[1]) : double.NaN;
            return JsValue.Number(SysMath.Atan2(y, x));
        }));

        math.DefineOwnProperty("pow", MethodDesc("pow", (_, args) =>
        {
            var b = args.Length > 0 ? JsValue.ToNumber(args[0]) : double.NaN;
            var e = args.Length > 1 ? JsValue.ToNumber(args[1]) : double.NaN;
            // Math.pow(NaN, 0) === 1 per ES spec (overrides usual NaN-poisoning).
            if (e == 0) return JsValue.Number(1);
            return JsValue.Number(SysMath.Pow(b, e));
        }));

        math.DefineOwnProperty("imul", MethodDesc("imul", (_, args) =>
        {
            var a = ToUint32(args.Length > 0 ? args[0] : JsValue.Undefined);
            var b = ToUint32(args.Length > 1 ? args[1] : JsValue.Undefined);
            // Truncate to Int32 after multiplication mod 2^32.
            unchecked
            {
                uint product = a * b;
                return JsValue.Number((int)product);
            }
        }));

        // ---------- Variadic methods -----------------------------------------
        math.DefineOwnProperty("max", MethodDesc("max", (_, args) =>
        {
            // Math.max() === -Infinity per spec.
            double best = double.NegativeInfinity;
            foreach (var arg in args)
            {
                var n = JsValue.ToNumber(arg);
                if (double.IsNaN(n)) return JsValue.NaN;
                // +0 should beat -0 for max.
                if (n > best || (n == 0 && best == 0 && !double.IsNegative(n)))
                    best = n;
            }
            return JsValue.Number(best);
        }));

        math.DefineOwnProperty("min", MethodDesc("min", (_, args) =>
        {
            // Math.min() === +Infinity per spec.
            double best = double.PositiveInfinity;
            foreach (var arg in args)
            {
                var n = JsValue.ToNumber(arg);
                if (double.IsNaN(n)) return JsValue.NaN;
                if (n < best || (n == 0 && best == 0 && double.IsNegative(n)))
                    best = n;
            }
            return JsValue.Number(best);
        }));

        math.DefineOwnProperty("hypot", MethodDesc("hypot", (_, args) =>
        {
            if (args.Length == 0) return JsValue.Number(0);
            // General variadic case: sum-of-squares with infinity/NaN guards.
            bool anyInf = false; bool anyNan = false;
            double sum = 0;
            foreach (var arg in args)
            {
                var n = JsValue.ToNumber(arg);
                if (double.IsInfinity(n)) anyInf = true;
                else if (double.IsNaN(n)) anyNan = true;
                else sum += n * n;
            }
            if (anyInf) return JsValue.Number(double.PositiveInfinity);
            if (anyNan) return JsValue.NaN;
            return JsValue.Number(SysMath.Sqrt(sum));
        }));

        // Stamp Math on the global object as writable + configurable + non-enumerable.
        realm.GlobalObject.DefineOwnProperty("Math",
            PropertyDescriptor.Data(JsValue.Object(math),
                writable: true, enumerable: false, configurable: true));
    }

    // ----------------------------------------------------------- helpers

    private static void DefineConst(JsObject math, string name, double value)
    {
        math.DefineOwnProperty(name,
            PropertyDescriptor.Data(JsValue.Number(value),
                writable: false, enumerable: false, configurable: false));
    }

    private static void DefineUnary(JsObject math, string name, Func<double, double> fn)
    {
        math.DefineOwnProperty(name, MethodDesc(name, (_, args) =>
        {
            var x = args.Length > 0 ? JsValue.ToNumber(args[0]) : double.NaN;
            return JsValue.Number(fn(x));
        }));
    }

    private static PropertyDescriptor MethodDesc(string name, Func<JsValue, JsValue[], JsValue> body)
        => PropertyDescriptor.Data(
            JsValue.Object(new JsNativeFunction(name, body, isConstructor: false)),
            writable: true, enumerable: false, configurable: true);

    /// <summary>JS-style Math.round: rounds half toward +∞, with NaN / ±Infinity
    /// / signed-zero passthrough. Implemented as <c>floor(x + 0.5)</c> with the
    /// usual edge-case handling.</summary>
    private static double JsRound(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return x;
        if (x == 0) return x; // preserves -0 / +0
        // Math.round(-0.5) === -0, Math.round(-0.4) === -0, … negative values
        // in [-0.5, 0] round to -0. Math.round(0.5) === 1.
        if (x < 0 && x >= -0.5) return -0.0;
        return SysMath.Floor(x + 0.5);
    }

    /// <summary>JS-style Math.sign: returns -0 for -0, +0 for +0, ±1 otherwise.</summary>
    private static double JsSign(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x > 0) return 1.0;
        if (x < 0) return -1.0;
        return x; // preserves signed zero
    }

    /// <summary>ToUint32 per §7.1.7 — used by Math.clz32 / Math.imul.</summary>
    private static uint ToUint32(JsValue value)
    {
        var n = JsValue.ToNumber(value);
        if (double.IsNaN(n) || double.IsInfinity(n) || n == 0) return 0;
        var i = SysMath.Truncate(n);
        // Modulo 2^32, then map to unsigned.
        var mod = i - SysMath.Floor(i / 4294967296.0) * 4294967296.0;
        return (uint)mod;
    }

    private static double Clz32(double x)
    {
        var u = ToUint32(JsValue.Number(x));
        if (u == 0) return 32;
        int c = 0;
        while ((u & 0x80000000u) == 0) { c++; u <<= 1; }
        return c;
    }
}
