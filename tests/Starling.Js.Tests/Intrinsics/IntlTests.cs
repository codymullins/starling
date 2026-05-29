using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests.Intrinsics;

[TestClass]
public class IntlTests
{
    [TestMethod]
    public void Intl_global_exposes_lite_constructors()
    {
        Eval("typeof Intl === 'object';").AsBool.Should().BeTrue();
        Eval("typeof Intl.DateTimeFormat === 'function';").AsBool.Should().BeTrue();
        Eval("typeof Intl.NumberFormat === 'function';").AsBool.Should().BeTrue();
        Eval("typeof Intl.Collator === 'function';").AsBool.Should().BeTrue();
        Eval("Intl.getCanonicalLocales(['en-us', 'bad-locale']).join('|');").AsString.Should().Be("en-US");
    }

    [TestMethod]
    public void DateTimeFormat_formats_utc_dates_and_resolves_supported_subset()
    {
        Eval("new Intl.DateTimeFormat('en-US').format(new Date(0));").AsString.Should().Be("1/1/1970");
        Eval("new Intl.DateTimeFormat('en-US', { year: 'numeric', month: '2-digit', day: '2-digit', timeZone: 'America/New_York' }).format(new Date(0));")
            .AsString.Should().Be("01/01/1970");
        Eval("new Intl.DateTimeFormat('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }).format(new Date(0));")
            .AsString.Should().Be("00:00:00");
        Eval("new Intl.DateTimeFormat('en-US', { hour: 'numeric', minute: '2-digit', hour12: true }).resolvedOptions().timeZone;")
            .AsString.Should().Be("UTC");
        Eval("Intl.DateTimeFormat.supportedLocalesOf(['en-US', 'fr-FR', 'missing-locale']).join('|');")
            .AsString.Should().Be("en-US|fr-FR");
    }

    [TestMethod]
    public void NumberFormat_formats_decimal_percent_currency_and_resolves_supported_subset()
    {
        Eval("new Intl.NumberFormat('en-US').format(1234.5);").AsString.Should().Be("1,234.5");
        Eval("Intl.NumberFormat('en-US', { useGrouping: false, minimumFractionDigits: 2 }).format(1234.5);")
            .AsString.Should().Be("1234.50");
        Eval("new Intl.NumberFormat('en-US', { style: 'percent' }).format(0.123);").AsString.Should().Be("12%");
        Eval("new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(1234.5);")
            .AsString.Should().Be("$1,234.50");
        Eval("new Intl.NumberFormat('en-US', { style: 'currency', currency: 'EUR' }).resolvedOptions().currency;")
            .AsString.Should().Be("EUR");
        Eval("Intl.NumberFormat.supportedLocalesOf('de-DE')[0];").AsString.Should().Be("de-DE");
    }

    [TestMethod]
    public void Collator_compares_and_resolves_supported_subset()
    {
        Eval("new Intl.Collator('en-US').compare('a', 'b') < 0;").AsBool.Should().BeTrue();
        Eval("new Intl.Collator('en-US', { sensitivity: 'base' }).compare('a', 'A');").AsNumber.Should().Be(0);
        Eval("new Intl.Collator('en-US', { usage: 'search', numeric: true }).resolvedOptions().usage;")
            .AsString.Should().Be("search");
        Eval("Intl.Collator.supportedLocalesOf(['en-US', 'zz-ZZ']).length;").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Format_and_compare_functions_are_instance_bound_for_bundle_compatibility()
    {
        Eval("var f = new Intl.NumberFormat('en-US').format; f(12.5);").AsString.Should().Be("12.5");
        Eval("var f = Intl.DateTimeFormat('en-US').format; f(0);").AsString.Should().Be("1/1/1970");
        Eval("var c = new Intl.Collator('en-US').compare; c('x', 'x');").AsNumber.Should().Be(0);
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
