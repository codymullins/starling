using AwesomeAssertions;
using Starling.Css.Selectors;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.Selectors;

/// <summary>
/// Selector conformance for <see href="https://drafts.csswg.org/selectors-4/">Selectors Level 4</see>.
/// </summary>
[TestClass]
[Spec("selectors", "https://drafts.csswg.org/selectors-4/")]
public sealed class SelectorTests
{

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#matches-pseudo"/>
    /// <para>Selector <c>:is()</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#matches-pseudo")]
    [SpecFact]
    public void Matches_is()
    {
        // Selectors 4 §3.3: :is() takes the specificity of its most-specific argument.
        var isSelector = SelectorParser.ParseSelectorList(":is(#hero, .card)").Selectors.Single();
        isSelector.Specificity.Should().Be(new Specificity(1, 0, 0));
        // Match in combination with other simple selectors.
        var doc = new Document();
        var root = doc.CreateElement("div");
        root.SetAttribute("lang", "en-US");
        var child = doc.CreateElement("p");
        child.ClassList.Add("intro");
        doc.AppendChild(root);
        root.AppendChild(child);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":is(p, a).intro"), child).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#negation-pseudo"/>
    /// <para>Selector <c>:not()</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#negation-pseudo")]
    [SpecFact]
    public void Matches_not()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        var child = doc.CreateElement("p");
        child.ClassList.Add("intro");
        doc.AppendChild(root);
        root.AppendChild(child);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("p:not(.hidden)"), child).Should().BeTrue();
        child.ClassList.Add("hidden");
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("p:not(.hidden)"), child).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#where-pseudo"/>
    /// <para>Selector <c>:where()</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#where-pseudo")]
    [SpecFact]
    public void Matches_where()
    {
        // Selectors 4 §3.4: :where() always has zero specificity.
        var where = SelectorParser.ParseSelectorList(":where(#hero)").Selectors.Single();
        where.Specificity.Should().Be(Specificity.Zero);
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#has-pseudo"/>
    /// <para>Selector <c>:has()</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#has-pseudo")]
    [SpecFact]
    public void Matches_has()
    {
        var doc = new Document();
        var section = doc.CreateElement("section");
        var a = doc.CreateElement("a");
        doc.AppendChild(section);
        section.AppendChild(a);
        // Selectors 4 §6.5.1: :has(> a) matches when a direct child matches the argument.
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("section:has(> a)"), section).Should().BeTrue();
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("section:has(> img)"), section).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#defined-pseudo"/>
    /// <para>Selector <c>:defined</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#defined-pseudo")]
    [SpecFact]
    public void Matches_defined()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        doc.AppendChild(div);
        // Selectors 4 §6.4.1: :defined matches built-in elements.
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":defined"), div).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#dir-pseudo"/>
    /// <para>Selector <c>:dir()</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#dir-pseudo")]
    [SpecFact]
    public void Matches_dir()
    {
        var doc = new Document();
        var div = doc.CreateElement("div");
        div.SetAttribute("dir", "rtl");
        doc.AppendChild(div);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":dir(rtl)"), div).Should().BeTrue();
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":dir(ltr)"), div).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#lang-pseudo"/>
    /// <para>Selector <c>:lang()</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#lang-pseudo")]
    [SpecFact]
    public void Matches_lang()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        root.SetAttribute("lang", "en-US");
        var child = doc.CreateElement("p");
        doc.AppendChild(root);
        root.AppendChild(child);
        // Selectors 4 §6.2.2: :lang(en) matches en-US via language-range subtag prefix.
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":lang(en)"), root).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#any-link-pseudo"/>
    /// <para>Selector <c>:any-link</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#any-link-pseudo")]
    [SpecFact]
    public void Matches_any_link()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        a.SetAttribute("href", "https://example.com/");
        doc.AppendChild(a);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":any-link"), a).Should().BeTrue();
        // No href → no match.
        var b = doc.CreateElement("a");
        doc.AppendChild(b);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":any-link"), b).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#link-pseudo"/>
    /// <para>Selector <c>:link</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#link-pseudo")]
    [SpecFact]
    public void Matches_link()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        a.SetAttribute("href", "https://example.com/");
        doc.AppendChild(a);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":link"), a).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#visited-pseudo"/>
    /// <para>Selector <c>:visited</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#visited-pseudo")]
    [SpecFact]
    public void Matches_visited()
    {
        var doc = new Document();
        var a = doc.CreateElement("a");
        a.SetAttribute("href", "https://example.com/");
        doc.AppendChild(a);
        // Per Selectors 4 §6.6: :visited never matches by default for privacy.
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":visited"), a).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#placeholder-shown-pseudo"/>
    /// <para>Selector <c>:placeholder-shown</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#placeholder-shown-pseudo")]
    [SpecFact]
    public void Matches_placeholder_shown()
    {
        var doc = new Document();
        var input = doc.CreateElement("input");
        input.SetAttribute("placeholder", "Type here");
        doc.AppendChild(input);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":placeholder-shown"), input).Should().BeTrue();
        input.SetAttribute("value", "abc");
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":placeholder-shown"), input).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#required-pseudo"/>
    /// <para>Selector <c>:required</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#required-pseudo")]
    [SpecFact]
    public void Matches_required()
    {
        var doc = new Document();
        var i1 = doc.CreateElement("input");
        i1.SetAttribute("required", "");
        doc.AppendChild(i1);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":required"), i1).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#optional-pseudo"/>
    /// <para>Selector <c>:optional</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#optional-pseudo")]
    [SpecFact]
    public void Matches_optional()
    {
        var doc = new Document();
        var i2 = doc.CreateElement("input");
        doc.AppendChild(i2);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList(":optional"), i2).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#nth-child-pseudo"/>
    /// <para>Selector <c>:nth-child()</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#nth-child-pseudo")]
    [SpecFact]
    public void Matches_nth_child()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        var first = doc.CreateElement("span");
        var second = doc.CreateElement("span");
        second.SetAttribute("data-tags", "alpha beta");
        doc.AppendChild(root);
        root.AppendChild(first);
        root.AppendChild(second);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("span:nth-child(2)[data-tags~=beta]"), second).Should().BeTrue();
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("span:nth-child(2)"), first).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#first-child-pseudo"/>
    /// <para>Selector <c>:first-child</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#first-child-pseudo")]
    [SpecFact]
    public void Matches_first_child()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        var first = doc.CreateElement("span");
        var second = doc.CreateElement("span");
        doc.AppendChild(root);
        root.AppendChild(first);
        root.AppendChild(second);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("span:first-child"), first).Should().BeTrue();
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("span:first-child"), second).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/selectors-4/#only-child-pseudo"/>
    /// <para>Selector <c>:only-child</c>.</para>
    /// </summary>
    [Spec("selectors", "https://drafts.csswg.org/selectors-4/#only-child-pseudo")]
    [SpecFact]
    public void Matches_only_child()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        var a = doc.CreateElement("span");
        var b = doc.CreateElement("span");
        doc.AppendChild(root);
        root.AppendChild(a);
        root.AppendChild(b);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("span:only-child"), a).Should().BeFalse();
        // Solo child does match.
        var solo = doc.CreateElement("section");
        var only = doc.CreateElement("span");
        doc.AppendChild(solo);
        solo.AppendChild(only);
        SelectorMatcher.Matches(SelectorParser.ParseSelectorList("span:only-child"), only).Should().BeTrue();
    }

}
