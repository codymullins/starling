using FluentAssertions;
using Starling.Css.Values;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-values-5", "https://www.w3.org/TR/css-values-5/")]

[TestClass]
public class AttrResolverTests
{
    private static Func<string, string?> Map(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.Key, p => (string?)p.Value);
        return name => dict.TryGetValue(name, out var v) ? v : null;
    }

    [TestMethod]
    public void Attr_default_type_is_string()
    {
        var attr = new CssAttrReference("data-name", null, null);
        var r = attr.Resolve(Map(("data-name", "hello world")));
        r.Should().BeOfType<CssString>().Which.Value.Should().Be("hello world");
    }

    [TestMethod]
    public void Attr_explicit_string_type()
    {
        var attr = new CssAttrReference("alt", "string", null);
        var r = attr.Resolve(Map(("alt", "logo")));
        r.Should().BeOfType<CssString>().Which.Value.Should().Be("logo");
    }

    [TestMethod]
    public void Attr_with_px_unit_returns_length()
    {
        var attr = new CssAttrReference("data-width", "px", null);
        var r = attr.Resolve(Map(("data-width", "120")));
        r.Should().BeOfType<CssLength>();
        var len = (CssLength)r!;
        len.Value.Should().Be(120);
        len.Unit.Should().Be(CssLengthUnit.Px);
    }

    [TestMethod]
    public void Attr_with_em_unit_returns_em_length()
    {
        var attr = new CssAttrReference("data-size", "em", null);
        var r = attr.Resolve(Map(("data-size", "1.5")));
        r.Should().BeOfType<CssLength>();
        var len = (CssLength)r!;
        len.Value.Should().Be(1.5);
        len.Unit.Should().Be(CssLengthUnit.Em);
    }

    [TestMethod]
    public void Attr_with_percentage()
    {
        var attr = new CssAttrReference("data-opacity", "%", null);
        var r = attr.Resolve(Map(("data-opacity", "75")));
        r.Should().BeOfType<CssPercentage>().Which.Value.Should().Be(75);
    }

    [TestMethod]
    public void Attr_with_number_type()
    {
        var attr = new CssAttrReference("data-count", "number", null);
        var r = attr.Resolve(Map(("data-count", "42.5")));
        r.Should().BeOfType<CssNumber>().Which.Value.Should().Be(42.5);
    }

    [TestMethod]
    public void Attr_with_integer_type()
    {
        var attr = new CssAttrReference("data-id", "integer", null);
        var r = attr.Resolve(Map(("data-id", "42")));
        r.Should().BeOfType<CssNumber>().Which.Value.Should().Be(42);
    }

    [TestMethod]
    public void Attr_integer_rejects_non_integer()
    {
        var fallback = new CssNumber(0);
        var attr = new CssAttrReference("data-id", "integer", fallback);
        var r = attr.Resolve(Map(("data-id", "3.14")));
        r.Should().BeSameAs(fallback);
    }

    [TestMethod]
    public void Attr_with_angle_type()
    {
        var attr = new CssAttrReference("data-rot", "deg", null);
        var r = attr.Resolve(Map(("data-rot", "45")));
        r.Should().BeOfType<CssAngle>();
        var angle = (CssAngle)r!;
        angle.Value.Should().Be(45);
        angle.Unit.Should().Be(CssAngleUnit.Degrees);
    }

    [TestMethod]
    public void Attr_with_time_type()
    {
        var attr = new CssAttrReference("data-delay", "ms", null);
        var r = attr.Resolve(Map(("data-delay", "250")));
        r.Should().BeOfType<CssTime>();
        var t = (CssTime)r!;
        t.Value.Should().Be(250);
        t.Unit.Should().Be(CssTimeUnit.Milliseconds);
    }

    [TestMethod]
    public void Attr_with_color_named()
    {
        var attr = new CssAttrReference("data-color", "color", null);
        var r = attr.Resolve(Map(("data-color", "red")));
        r.Should().BeOfType<CssColor>();
        var c = (CssColor)r!;
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
    }

    [TestMethod]
    public void Attr_with_color_hex()
    {
        var attr = new CssAttrReference("data-color", "color", null);
        var r = attr.Resolve(Map(("data-color", "#00ff00")));
        r.Should().BeOfType<CssColor>();
        var c = (CssColor)r!;
        c.G.Should().Be(255);
    }

    [TestMethod]
    public void Attr_color_invalid_uses_fallback()
    {
        var fallback = new CssKeyword("black");
        var attr = new CssAttrReference("data-color", "color", fallback);
        var r = attr.Resolve(Map(("data-color", "notacolor")));
        r.Should().BeSameAs(fallback);
    }

    [TestMethod]
    public void Attr_missing_attribute_uses_fallback()
    {
        var fallback = new CssLength(100, CssLengthUnit.Px);
        var attr = new CssAttrReference("data-width", "px", fallback);
        var r = attr.Resolve(Map());
        r.Should().BeSameAs(fallback);
    }

    [TestMethod]
    public void Attr_missing_attribute_no_fallback_returns_null()
    {
        var attr = new CssAttrReference("data-width", "px", null);
        var r = attr.Resolve(Map());
        r.Should().BeNull();
    }

    [TestMethod]
    public void Attr_url_type()
    {
        var attr = new CssAttrReference("href", "url", null);
        var r = attr.Resolve(Map(("href", "https://example.com")));
        r.Should().BeOfType<CssUrl>().Which.Value.Should().Be("https://example.com");
    }

    [TestMethod]
    public void Attr_unparseable_number_uses_fallback()
    {
        var fallback = new CssNumber(0);
        var attr = new CssAttrReference("data-count", "number", fallback);
        var r = attr.Resolve(Map(("data-count", "abc")));
        r.Should().BeSameAs(fallback);
    }

    [TestMethod]
    public void Attr_unknown_type_falls_back_to_string()
    {
        var attr = new CssAttrReference("data-x", "weird-type", null);
        var r = attr.Resolve(Map(("data-x", "value")));
        r.Should().BeOfType<CssString>().Which.Value.Should().Be("value");
    }

    [TestMethod]
    public void Coerce_directly_works_without_lookup()
    {
        var r = AttrResolver.Coerce("12.5", "px");
        r.Should().BeOfType<CssLength>().Which.Value.Should().Be(12.5);
    }
}
