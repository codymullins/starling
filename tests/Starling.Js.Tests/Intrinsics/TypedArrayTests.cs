using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

[TestClass]
public class TypedArrayTests
{
    [TestMethod]
    public void ArrayBuffer_allocates_reports_length_slices_and_detects_views()
    {
        Eval("var b = new ArrayBuffer(8); b.byteLength;").AsNumber.Should().Be(8);
        Eval("var b = new ArrayBuffer(8); var s = b.slice(2, 6); s.byteLength;").AsNumber.Should().Be(4);
        Eval("var b = new ArrayBuffer(4); var a = new Uint8Array(b); a[1] = 99; var s = b.slice(1, 3); var v = new Uint8Array(s); v[0];").AsNumber.Should().Be(99);
        Eval("ArrayBuffer.isView(new Uint8Array(2));").AsBool.Should().BeTrue();
        Eval("ArrayBuffer.isView(new DataView(new ArrayBuffer(2)));").AsBool.Should().BeTrue();
        Eval("ArrayBuffer.isView(new ArrayBuffer(2));").AsBool.Should().BeFalse();
        Action tooLarge = () => Eval("new ArrayBuffer(2147483648);");
        tooLarge.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void DataView_round_trips_numeric_types_endianness_and_bounds()
    {
        Eval("var d = new DataView(new ArrayBuffer(32)); d.setInt8(0, -1); d.getInt8(0);").AsNumber.Should().Be(-1);
        Eval("var d = new DataView(new ArrayBuffer(32)); d.setUint8(0, 255); d.getUint8(0);").AsNumber.Should().Be(255);
        Eval("var d = new DataView(new ArrayBuffer(32)); d.setInt16(0, 0x1234, false); d.getUint8(0) + d.getUint8(1);").AsNumber.Should().Be(0x12 + 0x34);
        Eval("var d = new DataView(new ArrayBuffer(32)); d.setInt16(0, 0x1234, true); d.getUint8(0);").AsNumber.Should().Be(0x34);
        Eval("var d = new DataView(new ArrayBuffer(32)); d.setUint16(0, 65535, true); d.getUint16(0, true);").AsNumber.Should().Be(65535);
        Eval("var d = new DataView(new ArrayBuffer(32)); d.setInt32(0, -123456, true); d.getInt32(0, true);").AsNumber.Should().Be(-123456);
        Eval("var d = new DataView(new ArrayBuffer(32)); d.setUint32(0, 4000000000, false); d.getUint32(0, false);").AsNumber.Should().Be(4000000000);
        Eval("var d = new DataView(new ArrayBuffer(32)); d.setFloat32(0, 1.5, true); d.getFloat32(0, true);").AsNumber.Should().BeApproximately(1.5, 0.0001);
        Eval("var d = new DataView(new ArrayBuffer(32)); d.setFloat64(0, -3.25, false); d.getFloat64(0, false);").AsNumber.Should().Be(-3.25);
        Eval("var d = new DataView(new ArrayBuffer(32)); d.setBigInt64(0, 12345, true); d.getBigInt64(0, true);").AsNumber.Should().Be(12345);
        Eval("var d = new DataView(new ArrayBuffer(32)); d.setBigUint64(0, 12345, false); d.getBigUint64(0, false);").AsNumber.Should().Be(12345);
        Eval("var b = new ArrayBuffer(8); var d = new DataView(b, 2, 4); d.byteOffset + d.byteLength + d.buffer.byteLength;").AsNumber.Should().Be(14);
        Action badCtor = () => Eval("new DataView(new ArrayBuffer(4), 5);");
        Action badRead = () => Eval("var d = new DataView(new ArrayBuffer(4)); d.getFloat64(0);");
        badCtor.Should().Throw<JsThrow>();
        badRead.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void TypedArray_constructors_cover_length_buffer_array_like_and_copy()
    {
        Eval("var a = new Uint8Array(3); a.length + a.byteLength + a.byteOffset;").AsNumber.Should().Be(6);
        Eval("var b = new ArrayBuffer(4); var a = new Uint8Array(b); var c = new Uint8Array(b); a[0] = 7; c[0];").AsNumber.Should().Be(7);
        Eval("var b = new ArrayBuffer(8); var a = new Uint16Array(b, 2, 2); a.length + a.byteOffset + a.byteLength;").AsNumber.Should().Be(8);
        Eval("var a = new Uint8Array({0: 5, 1: 6, length: 2}); a[0] + a[1] + a.length;").AsNumber.Should().Be(13);
        Eval("var a = new Uint8Array({0: 5, 1: 6, length: 2}); var b = new Uint8Array(a); a[0] = 9; b[0];").AsNumber.Should().Be(5);
        Eval("Uint8Array.of(1, 2, 3).length;").AsNumber.Should().Be(3);
        Eval("Uint8Array.from({0: 2, 1: 3, length: 2}, function(v) { return v * 2; })[1];").AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void TypedArray_indexed_access_converts_per_element_type()
    {
        Eval("var a = new Uint8Array(1); a[0] = 300; a[0];").AsNumber.Should().Be(44);
        Eval("var a = new Int8Array(1); a[0] = 255; a[0];").AsNumber.Should().Be(-1);
        Eval("var a = new Uint16Array(1); a[0] = 65537; a[0];").AsNumber.Should().Be(1);
        Eval("var a = new Int16Array(1); a[0] = 65535; a[0];").AsNumber.Should().Be(-1);
        Eval("var a = new Uint32Array(1); a[0] = -1; a[0];").AsNumber.Should().Be(4294967295);
        Eval("var a = new Int32Array(1); a[0] = 4294967295; a[0];").AsNumber.Should().Be(-1);
        Eval("var a = new Float32Array(1); a[0] = 1.25; a[0];").AsNumber.Should().BeApproximately(1.25, 0.0001);
        Eval("var a = new Float64Array(1); a[0] = -9.5; a[0];").AsNumber.Should().Be(-9.5);
        Eval("var a = new BigInt64Array(1); a[0] = -99; a[0];").AsNumber.Should().Be(-99);
        Eval("var a = new BigUint64Array(1); a[0] = 99; a[0];").AsNumber.Should().Be(99);
    }

    [TestMethod]
    public void Uint8ClampedArray_clamps_and_rounds_values()
    {
        Eval("var a = new Uint8ClampedArray(4); a[0] = NaN; a[1] = 1000; a[2] = -1; a[3] = 2.5; a[0] + a[1] + a[2] + a[3];").AsNumber.Should().Be(257);
    }

    [TestMethod]
    public void Subarray_shares_buffer_and_slice_copies()
    {
        Eval("var a = new Uint8Array({0:1,1:2,2:3,length:3}); var s = a.subarray(1); s[0] = 9; a[1];").AsNumber.Should().Be(9);
        Eval("var a = new Uint8Array({0:1,1:2,2:3,length:3}); var s = a.slice(1); s[0] = 9; a[1];").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Prototype_methods_cover_mutation_search_callbacks_and_copying()
    {
        Eval("var a = new Uint8Array(3); a.fill(4); a[0] + a[1] + a[2];").AsNumber.Should().Be(12);
        Eval("var a = new Uint8Array(4); a.set({0:7,1:8,length:2}, 1); a[0] + a[1] + a[2];").AsNumber.Should().Be(15);
        Eval("var a = new Uint8Array({0:3,1:4,2:3,length:3}); a.indexOf(3);").AsNumber.Should().Be(0);
        Eval("var a = new Uint8Array({0:3,1:4,2:3,length:3}); a.lastIndexOf(3);").AsNumber.Should().Be(2);
        Eval("var a = new Uint8Array({0:3,1:4,length:2}); a.includes(4);").AsBool.Should().BeTrue();
        Eval("var a = new Uint8Array({0:1,1:2,2:3,length:3}); a.forEach(function(v, i, o) { o[i] = v + 1; }); a[2];").AsNumber.Should().Be(4);
        Eval("var a = new Uint8Array({0:1,1:2,2:3,length:3}); var b = a.map(function(v) { return v + 1; }); b[0] + b[2] + b.length;").AsNumber.Should().Be(9);
        Eval("var a = new Uint8Array({0:1,1:2,2:3,3:4,length:4}); var b = a.filter(function(v) { return v > 2; }); b[0] + b[1] + b.length;").AsNumber.Should().Be(9);
        Eval("var a = new Uint8Array({0:1,1:2,2:3,length:3}); a.reduce(function(acc, v) { return acc + v; }, 10);").AsNumber.Should().Be(16);
        Eval("var a = new Uint8Array({0:1,1:2,2:3,length:3}); a.every(function(v) { return v > 0; });").AsBool.Should().BeTrue();
        Eval("var a = new Uint8Array({0:1,1:2,2:3,length:3}); a.some(function(v) { return v == 2; });").AsBool.Should().BeTrue();
        Eval("var a = new Uint8Array({0:1,1:2,2:3,length:3}); a.find(function(v) { return v > 1; });").AsNumber.Should().Be(2);
        Eval("var a = new Uint8Array({0:1,1:2,2:3,length:3}); a.findIndex(function(v) { return v > 1; });").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Prototype_methods_cover_iteration_join_order_and_bytes_per_element()
    {
        Eval("var a = new Uint8Array({0:3,1:1,2:2,length:3}); a.sort(); a.join('-');").AsString.Should().Be("1-2-3");
        Eval("var a = new Uint8Array({0:1,1:2,2:3,length:3}); a.reverse(); a.join();").AsString.Should().Be("3,2,1");
        Eval("var a = new Uint8Array({0:1,1:2,2:3,length:3}); var b = a.toReversed(); b[0] + a[0];").AsNumber.Should().Be(4);
        Eval("var a = new Uint8Array({0:3,1:1,2:2,length:3}); var b = a.toSorted(); b[0] + a[0];").AsNumber.Should().Be(4);
        Eval("var a = new Uint8Array({0:1,1:2,length:2}); a.keys()[1];").AsNumber.Should().Be(1);
        Eval("var a = new Uint8Array({0:1,1:2,length:2}); a.values()[1];").AsNumber.Should().Be(2);
        Eval("var a = new Uint8Array({0:1,1:2,length:2}); a.entries()[1][0] + a.entries()[1][1];").AsNumber.Should().Be(3);
        Eval("var a = new Uint8Array({0:1,1:2,length:2}); var b = a['with'](1, 9); b[1] + a[1];").AsNumber.Should().Be(11);
        Eval("Uint8Array.BYTES_PER_ELEMENT + Int16Array.BYTES_PER_ELEMENT + Uint32Array.BYTES_PER_ELEMENT + Float64Array.BYTES_PER_ELEMENT;").AsNumber.Should().Be(15);
        Eval("new BigUint64Array(1).BYTES_PER_ELEMENT;").AsNumber.Should().Be(8);
    }

    [Ignore("B4-3 BigInt")]

    [TestMethod]
    public void DataView_BigInt_large_values_round_trip_as_bigints()
    {
        Eval("var d = new DataView(new ArrayBuffer(8)); d.setBigUint64(0, 9007199254740993, true); d.getBigUint64(0, true);").AsNumber.Should().Be(9007199254740993d);
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
