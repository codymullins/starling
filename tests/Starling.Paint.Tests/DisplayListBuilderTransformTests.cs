using FluentAssertions;
using Tessera.Css.Cascade;
using Tessera.Html;
using Tessera.Layout;
using Tessera.Layout.Text;
using Tessera.Paint.DisplayList;
using Xunit;

namespace Tessera.Paint.Tests;

public sealed class DisplayListBuilderTransformTests
{
    private static Tessera.Paint.DisplayList.DisplayList Build(string html)
    {
        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new Size(400, 400));
        return new DisplayListBuilder().Build(root);
    }

    [Fact]
    public void Boxes_without_transform_emit_no_transform_bracket()
    {
        var dl = Build("<body><div style=\"background-color:#ff0000;width:100px;height:100px\">x</div></body>");
        dl.Items.OfType<PushTransform>().Should().BeEmpty();
        dl.Items.OfType<PopTransform>().Should().BeEmpty();
    }

    [Fact]
    public void Translate_transform_emits_balanced_push_pop_with_translation()
    {
        var dl = Build("<body><div style=\"background-color:#ff0000;width:100px;height:100px;transform:translate(50px,20px)\">x</div></body>");
        var pushes = dl.Items.OfType<PushTransform>().ToList();
        var pops = dl.Items.OfType<PopTransform>().ToList();
        pushes.Should().HaveCount(pops.Count);
        pushes.Should().NotBeEmpty();
        var first = pushes[0];
        first.Matrix.IsIdentity.Should().BeFalse();
        // translate keeps the linear part as identity (a=1,b=0,c=0,d=1)
        first.Matrix.A.Should().Be(1);
        first.Matrix.B.Should().Be(0);
        first.Matrix.C.Should().Be(0);
        first.Matrix.D.Should().Be(1);
        first.Matrix.E.Should().BeApproximately(50, 0.001);
        first.Matrix.F.Should().BeApproximately(20, 0.001);
    }

    [Fact]
    public void Rotate_transform_bakes_centre_origin_into_matrix()
    {
        // 90deg rotation around centre of a 100x100 box: a point at (0,0) in
        // local space ends up at (centre - rotated half-extent). With centre
        // (50,50): the top-left corner maps to (0,100) under rotate(90deg)
        // around the centre, so translation component E should be 100, F = 0.
        var dl = Build("<body><div style=\"background-color:#ff0000;width:100px;height:100px;transform:rotate(90deg)\">x</div></body>");
        var push = dl.Items.OfType<PushTransform>().First();
        // cos(90)=0, sin(90)=1 → a=0,b=1,c=-1,d=0
        push.Matrix.A.Should().BeApproximately(0, 0.001);
        push.Matrix.B.Should().BeApproximately(1, 0.001);
        push.Matrix.C.Should().BeApproximately(-1, 0.001);
        push.Matrix.D.Should().BeApproximately(0, 0.001);
    }
}
