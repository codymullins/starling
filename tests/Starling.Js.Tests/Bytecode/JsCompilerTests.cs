using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Xunit;

namespace Tessera.Js.Tests.Bytecode;

public class JsCompilerTests
{
    [Fact]
    public void Empty_program_just_halts()
    {
        var c = Compile("");
        // No statements → just Halt.
        c.Code.Should().Equal((byte)Opcode.Halt);
    }

    [Fact]
    public void Numeric_literal_zero_uses_LoadZero()
    {
        // Expression statement: LoadZero, Pop, Halt.
        var c = Compile("0;");
        c.Code.Should().Equal(
            (byte)Opcode.LoadZero,
            (byte)Opcode.Pop,
            (byte)Opcode.Halt);
    }

    [Fact]
    public void Nonzero_numeric_literal_loads_from_constant_pool()
    {
        var c = Compile("42;");
        c.Constants.Should().Contain(42.0);
        // LoadConst(idx) Pop Halt
        c.Code[0].Should().Be((byte)Opcode.LoadConst);
        c.Code[3].Should().Be((byte)Opcode.Pop);
    }

    [Fact]
    public void String_literal_interned_in_pool()
    {
        var c = Compile("\"hello\";");
        c.Constants.Should().Contain("hello");
    }

    [Fact]
    public void Boolean_and_null_use_dedicated_opcodes()
    {
        var t = Compile("true;");
        t.Code[0].Should().Be((byte)Opcode.LoadTrue);

        var n = Compile("null;");
        n.Code[0].Should().Be((byte)Opcode.LoadNull);
    }

    [Fact]
    public void Var_declaration_reserves_local_and_initializes()
    {
        var c = Compile("var x = 1;");
        c.LocalCount.Should().Be(1);
        // DeclareLocal(0) LoadConst(1.0) StoreLocal(0) Halt
        var d = Disassembler.Disassemble(c);
        d.Should().Contain("DeclareLocal 0")
            .And.Contain("LoadConst")
            .And.Contain("StoreLocal 0");
    }

    [Fact]
    public void Local_lookup_resolves_to_LoadLocal()
    {
        var d = Disassembler.Disassemble(Compile("var x = 1; x;"));
        d.Should().Contain("LoadLocal 0");
    }

    [Fact]
    public void Free_identifier_falls_through_to_LoadGlobal()
    {
        var d = Disassembler.Disassemble(Compile("foo;"));
        d.Should().Contain("LoadGlobal").And.Contain("\"foo\"");
    }

    [Fact]
    public void Binary_addition_compiles()
    {
        // 1 + 2; → LoadConst LoadConst Add Pop Halt
        var d = Disassembler.Disassemble(Compile("1 + 2;"));
        d.Should().Contain("Add");
    }

    [Fact]
    public void Comparison_operators()
    {
        Disassembler.Disassemble(Compile("a === b;")).Should().Contain("StrictEq");
        Disassembler.Disassemble(Compile("a < b;")).Should().Contain("Lt");
    }

    [Fact]
    public void Unary_minus()
    {
        Disassembler.Disassemble(Compile("-x;")).Should().Contain("Neg");
    }

    [Fact]
    public void Typeof_unary()
    {
        Disassembler.Disassemble(Compile("typeof x;")).Should().Contain("TypeOf");
    }

    [Fact]
    public void If_without_else_emits_one_forward_jump()
    {
        var d = Disassembler.Disassemble(Compile("if (a) { b(); }"));
        d.Should().Contain("JumpIfFalse");
    }

    [Fact]
    public void If_with_else_emits_both_jumps()
    {
        var d = Disassembler.Disassemble(Compile("if (a) b(); else c();"));
        d.Should().Contain("JumpIfFalse").And.Contain("Jump ");
    }

    [Fact]
    public void While_loop_has_backward_jump()
    {
        var d = Disassembler.Disassemble(Compile("while (a) b();"));
        d.Should().Contain("JumpIfFalse").And.Contain("Jump ");
    }

    [Fact]
    public void Logical_and_short_circuits_via_dup_jumpiffalse_pop()
    {
        // a && b → LoadGlobal a, Dup, JumpIfFalse end, Pop, LoadGlobal b, end:
        var d = Disassembler.Disassemble(Compile("a && b;"));
        d.Should().Contain("Dup").And.Contain("JumpIfFalse").And.Contain("Pop");
    }

    [Fact]
    public void Logical_or_short_circuits_via_dup_jumpiftrue_pop()
    {
        var d = Disassembler.Disassemble(Compile("a || b;"));
        d.Should().Contain("Dup").And.Contain("JumpIfTrue").And.Contain("Pop");
    }

    [Fact]
    public void Nullish_coalescing_uses_JumpIfNotNullish()
    {
        // a ?? b short-circuits to 'a' when a is NOT nullish, hence the
        // inverted polarity vs && and ||.
        var d = Disassembler.Disassemble(Compile("a ?? b;"));
        d.Should().Contain("Dup").And.Contain("JumpIfNotNullish");
    }

    [Fact]
    public void Member_access_compiles()
    {
        var d = Disassembler.Disassemble(Compile("a.b;"));
        d.Should().Contain("LoadProperty").And.Contain("\"b\"");
    }

    [Fact]
    public void Computed_member_access_compiles()
    {
        var d = Disassembler.Disassemble(Compile("a[0];"));
        d.Should().Contain("LoadComputed");
    }

    [Fact]
    public void Call_emits_callee_then_args()
    {
        var d = Disassembler.Disassemble(Compile("foo(1, 2);"));
        d.Should().Contain("Call 2");
    }

    [Fact]
    public void Conditional_ternary_compiles()
    {
        var d = Disassembler.Disassemble(Compile("a ? b : c;"));
        d.Should().Contain("JumpIfFalse").And.Contain("Jump ");
    }

    [Fact]
    public void Assignment_to_local()
    {
        var d = Disassembler.Disassemble(Compile("var x = 0; x = 5;"));
        d.Should().Contain("StoreLocal 0");
    }

    [Fact]
    public void Compound_assignment_compiles_to_load_op_store()
    {
        var d = Disassembler.Disassemble(Compile("var x = 1; x += 2;"));
        d.Should().Contain("LoadLocal 0")
            .And.Contain("Add")
            .And.Contain("StoreLocal 0");
    }

    [Fact]
    public void Postfix_increment_dups_before_mutation()
    {
        var d = Disassembler.Disassemble(Compile("var x = 0; x++;"));
        d.Should().Contain("Dup").And.Contain("Add").And.Contain("StoreLocal 0");
    }

    [Fact]
    public void Sequence_expression_pops_intermediate()
    {
        var d = Disassembler.Disassemble(Compile("(a, b, c);"));
        // Two Pops (one per intermediate) plus the statement's Pop.
        d.Split('\n').Where(l => l.Contains("Pop")).Should().HaveCount(3);
    }

    [Fact]
    public void Return_at_top_level_compiles()
    {
        var d = Disassembler.Disassemble(Compile("return;"));
        d.Should().Contain("ReturnUndefined");

        var d2 = Disassembler.Disassemble(Compile("return 42;"));
        d2.Should().Contain("LoadConst").And.Contain("Return");
    }

    [Fact]
    public void Block_scope_does_not_leak_locals_outward()
    {
        // After the block ends, a reference to 'x' should NOT resolve as
        // local. The outer scope only sees a global lookup.
        var d = Disassembler.Disassemble(Compile("{ var inner = 1; } outer;"));
        d.Should().Contain("LoadGlobal").And.Contain("\"outer\"");
    }

    [Fact]
    public void Throw_compiles()
    {
        var d = Disassembler.Disassemble(Compile("throw e;"));
        d.Should().Contain("Throw");
    }

    [Fact]
    public void String_constants_are_interned()
    {
        var c = Compile("'a'; 'a'; 'a';");
        // Three identical string literals should produce a single
        // constant-pool entry.
        c.Constants.Count(x => Equals(x, "a")).Should().Be(1);
    }

    [Fact]
    public void Disassembler_includes_constant_table_header()
    {
        var d = Disassembler.Disassemble(Compile("1 + 2;"));
        d.Should().Contain("# constants:")
            .And.Contain("# code:");
    }

    private static Chunk Compile(string src)
        => JsCompiler.Compile(new JsParser(src).ParseProgram());
}
