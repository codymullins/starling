using AwesomeAssertions;
using Starling.Css.Cssom;

namespace Starling.Css.Spec.Tests.CssCssom1;

/// <summary>
/// Behavioral conformance for the <c>CSSStyleDeclaration</c> interface of
/// <see href="https://drafts.csswg.org/cssom/">CSSOM</see> §6.4, exercised
/// through <see cref="CssomDeclarationBlock"/>.
/// </summary>
[TestClass]
[Spec("cssom-1", "https://drafts.csswg.org/cssom/", section: "6.4")]
public sealed class StyleDeclarationTests
{
    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-setproperty", section: "6.4.2")]
    [SpecFact]
    public void SetProperty_then_getPropertyValue_round_trips()
    {
        var block = new CssomDeclarationBlock();
        block.SetProperty("width", "10px", null);
        block.GetPropertyValue("width").Should().Be("10px");
    }

    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-setproperty", section: "6.4.2")]
    [SpecFact]
    public void SetProperty_is_case_insensitive_on_the_property_name()
    {
        var block = new CssomDeclarationBlock();
        block.SetProperty("WIDTH", "10px", null);
        block.GetPropertyValue("width").Should().Be("10px");
    }

    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-getpropertypriority", section: "6.4.3")]
    [SpecFact]
    public void Important_priority_is_recorded_and_reported()
    {
        var block = new CssomDeclarationBlock();
        block.SetProperty("color", "red", "important");
        block.GetPropertyPriority("color").Should().Be("important");
        block.SetProperty("margin", "0", null);
        block.GetPropertyPriority("margin").Should().BeEmpty();
    }

    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-removeproperty", section: "6.4.5")]
    [SpecFact]
    public void RemoveProperty_returns_old_value_and_drops_the_entry()
    {
        var block = new CssomDeclarationBlock();
        block.SetProperty("width", "10px", null);
        block.RemoveProperty("width").Should().Be("10px");
        block.GetPropertyValue("width").Should().BeEmpty();
        block.Count.Should().Be(0);
    }

    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-setproperty", section: "6.4.2")]
    [SpecFact]
    public void SetProperty_with_empty_value_removes_the_property()
    {
        var block = new CssomDeclarationBlock();
        block.SetProperty("width", "10px", null);
        block.SetProperty("width", "", null);
        block.GetPropertyValue("width").Should().BeEmpty();
    }

    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-item", section: "6.4.1")]
    [SpecFact]
    public void Length_and_item_enumerate_declarations_in_order()
    {
        var block = new CssomDeclarationBlock();
        block.SetProperty("width", "10px", null);
        block.SetProperty("height", "20px", null);
        block.Count.Should().Be(2);
        block.ItemName(0).Should().Be("width");
        block.ItemName(1).Should().Be("height");
        block.ItemName(5).Should().BeEmpty();
    }

    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-csstext", section: "6.4.1")]
    [SpecFact]
    public void CssText_round_trips_through_parse_and_serialize()
    {
        var block = new CssomDeclarationBlock { CssText = "width: 10px; height: 20px" };
        block.Count.Should().Be(2);
        block.GetPropertyValue("width").Should().Be("10px");
        block.GetPropertyValue("height").Should().Be("20px");
        block.CssText.Should().Be("width: 10px; height: 20px;");
    }

    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#dom-cssstyledeclaration-setproperty", section: "6.4.2")]
    [SpecFact]
    public void Custom_property_value_is_stored_verbatim()
    {
        var block = new CssomDeclarationBlock();
        block.SetProperty("--brand", "#036", null);
        block.GetPropertyValue("--brand").Should().Be("#036");
    }
}
