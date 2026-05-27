using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

[TestClass]
public class JsCapturedLexicalRepro
{
    [TestMethod]
    [DataRow("block", "(function(){ { let n = 1; var f = ()=>n; return f(); } })()")]
    [DataRow("switchblk", "(function(x){ switch(x){ case 1: { let n=1; return (()=>n)(); } default: return 0; } })(1)")]
    [DataRow("switchbare", "(function(x){ switch(x){ case 1: let n=1; return (()=>n)(); default: return 0; } })(1)")]
    [DataRow("tryblk", "(function(){ try { let n=1; return (()=>n)(); } catch(e){ return 0; } })()")]
    [DataRow("forofbody", "(function(){ for (var x of [1]) { let n=x; return (()=>n)(); } })()")]
    [DataRow("forbody", "(function(){ for (var i=0;i<1;i++) { let n=9; return (()=>n)(); } })()")]
    [DataRow("whilebody", "(function(){ while(true) { let n=1; return (()=>n)(); } })()")]
    [DataRow("dowhile", "(function(){ do { let n=1; return (()=>n)(); } while(false); })()")]
    [DataRow("fndeclblk", "(function(){ { let n=1; function g(){return n;} return g(); } })()")]
    [DataRow("labeled", "(function(){ L: { let n=1; return (()=>n)(); } })()")]
    [DataRow("ifblk", "(function(){ if (true) { let n=1; return (()=>n)(); } })()")]
    [DataRow("shadow", "(function(){ let n=1; var a=()=>n; { let n=2; var b=()=>n; return a()+b(); } })()")]
    [DataRow("nestedblk", "(function(){ { { let n=1; return (()=>n)(); } } })()")]
    [DataRow("condblk", "(function(){ for(;;){ { let n=1; return (()=>n)(); } } })()")]
    public void Captured_lexical_in_construct_compiles_and_runs(string label, string src)
    {
        var act = () => Eval(src);
        act.Should().NotThrow(label);
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
