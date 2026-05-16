using FluentAssertions;
using Xunit;

namespace Tessera.Common.Tests;

public class ResultTests
{
    [Fact]
    public void Ok_carries_value_and_reports_ok()
    {
        var r = Result<int, string>.Ok(42);
        r.IsOk.Should().BeTrue();
        r.IsErr.Should().BeFalse();
        r.Value.Should().Be(42);
    }

    [Fact]
    public void Err_carries_error_and_reports_err()
    {
        var r = Result<int, string>.Err("nope");
        r.IsErr.Should().BeTrue();
        r.Error.Should().Be("nope");
    }

    [Fact]
    public void Reading_Value_on_Err_throws()
    {
        var r = Result<int, string>.Err("nope");
        var act = () => _ = r.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Match_picks_correct_branch()
    {
        Result<int, string>.Ok(7).Match(v => v * 2, _ => -1).Should().Be(14);
        Result<int, string>.Err("x").Match(v => v * 2, _ => -1).Should().Be(-1);
    }
}

public class MaybeTests
{
    [Fact]
    public void Some_carries_value()
    {
        var m = Maybe<string>.Some("hi");
        m.HasValue.Should().BeTrue();
        m.Value.Should().Be("hi");
    }

    [Fact]
    public void None_has_no_value_and_throws_on_Value()
    {
        var m = Maybe<string>.None;
        m.HasValue.Should().BeFalse();
        var act = () => _ = m.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OrElse_returns_fallback_for_None()
    {
        Maybe<int>.None.OrElse(99).Should().Be(99);
        Maybe<int>.Some(1).OrElse(99).Should().Be(1);
    }
}
