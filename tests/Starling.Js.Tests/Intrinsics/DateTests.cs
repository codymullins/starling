using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

/// <summary>JS-surface tests for the Date intrinsic (B4-2). Uses the invariant
/// UTC locale; getTimezoneOffset always returns 0 here, so local + UTC getters
/// produce identical values.</summary>
public class DateTests
{
    [Fact]
    public void Date_now_returns_finite_number()
    {
        var v = Run("Date.now();");
        v.IsNumber.Should().BeTrue();
        double.IsFinite(v.AsNumber).Should().BeTrue();
    }

    [Fact]
    public void Date_epoch_isoString_is_1970()
    {
        Run("new Date(0).toISOString();").AsString.Should().Be("1970-01-01T00:00:00.000Z");
    }

    [Fact]
    public void Date_parse_iso_and_getter()
    {
        Run("new Date('2024-01-15T10:30:00Z').getUTCFullYear();").AsNumber.Should().Be(2024);
        Run("new Date('2024-01-15T10:30:00Z').getUTCHours();").AsNumber.Should().Be(10);
    }

    [Fact]
    public void Date_parse_invalid_string_is_NaN()
    {
        double.IsNaN(Run("new Date('not a date').getTime();").AsNumber).Should().BeTrue();
    }

    [Fact]
    public void Date_year_month_day_constructor()
    {
        Run("new Date(2024, 0, 15).getMonth();").AsNumber.Should().Be(0);
        Run("new Date(2024, 0, 15).getDate();").AsNumber.Should().Be(15);
        Run("new Date(2024, 11, 31).getMonth();").AsNumber.Should().Be(11);
        Run("new Date(2024, 0, 15).getFullYear();").AsNumber.Should().Be(2024);
    }

    [Fact]
    public void Date_parse_matches_constructor_string_overload()
    {
        Run("Date.parse('2024-01-15') - new Date('2024-01-15').getTime();").AsNumber.Should().Be(0);
    }

    [Fact]
    public void Date_getDay_returns_weekday()
    {
        // 2024-01-15 is a Monday.
        Run("new Date('2024-01-15T00:00:00Z').getUTCDay();").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Date_setFullYear_mutates_and_returns_new_timestamp()
    {
        Run("var d = new Date(0); d.setFullYear(2030); d.getUTCFullYear();").AsNumber.Should().Be(2030);
    }

    [Fact]
    public void Date_setTime_replaces_time_value()
    {
        Run("var d = new Date(0); d.setTime(86400000); d.getUTCDate();").AsNumber.Should().Be(2);
    }

    [Fact]
    public void Date_json_stringify_emits_isoString()
    {
        Run("JSON.stringify(new Date(0));").AsString.Should().Be("\"1970-01-01T00:00:00.000Z\"");
    }

    [Fact]
    public void Date_toString_starts_with_weekday()
    {
        var s = Run("new Date(0).toString();").AsString;
        s.Should().StartWith("Thu Jan 01 1970");
        s.Should().Contain("GMT+0000");
    }

    [Fact]
    public void Date_toUTCString_includes_GMT()
    {
        var s = Run("new Date(0).toUTCString();").AsString;
        s.Should().Be("Thu, 01 Jan 1970 00:00:00 GMT");
    }

    [Fact]
    public void Date_UTC_equals_iso_z_string_parse()
    {
        Run("Date.UTC(2024, 0, 15) === new Date('2024-01-15T00:00:00Z').getTime();").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Date_toISOString_throws_on_invalid()
    {
        Action act = () => Run("new Date('not a date').toISOString();");
        act.Should().Throw<JsThrow>();
    }

    [Fact]
    public void Date_valueOf_returns_ms()
    {
        Run("new Date(12345).valueOf();").AsNumber.Should().Be(12345);
    }

    [Fact]
    public void Date_call_without_new_returns_string()
    {
        Run("typeof Date();").AsString.Should().Be("string");
    }

    [Fact]
    public void Date_getTimezoneOffset_is_zero()
    {
        Run("new Date(0).getTimezoneOffset();").AsNumber.Should().Be(0);
    }

    [Fact]
    public void Date_toDateString_format()
    {
        Run("new Date(0).toDateString();").AsString.Should().Be("Thu Jan 01 1970");
    }

    [Fact]
    public void Date_toTimeString_format()
    {
        var s = Run("new Date(0).toTimeString();").AsString;
        s.Should().StartWith("00:00:00");
        s.Should().Contain("GMT+0000");
    }

    [Fact]
    public void Date_toLocale_methods_delegate_to_non_locale()
    {
        // Invariant-locale simplification: toLocaleString === toString.
        Run("new Date(0).toLocaleString() === new Date(0).toString();").AsBool.Should().BeTrue();
        Run("new Date(0).toLocaleDateString() === new Date(0).toDateString();").AsBool.Should().BeTrue();
        Run("new Date(0).toLocaleTimeString() === new Date(0).toTimeString();").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Date_with_only_year_yyyy_mm_dd()
    {
        // YYYY-MM-DD parses as UTC midnight.
        Run("new Date('2024-01-15').getUTCFullYear();").AsNumber.Should().Be(2024);
        Run("new Date('2024-01-15').getUTCHours();").AsNumber.Should().Be(0);
    }

    private static JsValue Run(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
