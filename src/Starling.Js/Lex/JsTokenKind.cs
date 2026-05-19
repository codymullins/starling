namespace Tessera.Js.Lex;

/// <summary>
/// Every token category the lexer can emit. Mirrors ECMAScript §12
/// (<see href="https://tc39.es/ecma262/#sec-ecmascript-language-lexical-grammar"/>).
/// </summary>
/// <remarks>
/// Reserved-word categories are first-class kinds so the parser doesn't have
/// to re-match identifier text. Contextual keywords (let, async, await, get,
/// set, of, from, as, target, meta, static) are emitted as
/// <see cref="Identifier"/> with the actual lexeme; the parser disambiguates
/// based on grammatical position.
/// </remarks>
public enum JsTokenKind
{
    // ----- Literal categories -------------------------------------------
    Identifier,
    PrivateIdentifier,  // #name — class private fields/methods
    NumericLiteral,
    BigIntLiteral,
    StringLiteral,
    NullLiteral,
    BooleanLiteral, // true / false
    RegExpLiteral,  // /pattern/flags (parser-driven; see JsLexer.ScanRegExp)

    // ----- Template literal pieces (§12.8.6) ---------------------------
    // The parser drives template lexing via JsLexer.ScanTemplate so that
    // ${ ... } substitution expressions can be lexed in normal-expression
    // mode. Each backtick-delimited literal yields:
    //   TemplateNoSubstitution            -- `…`
    //   TemplateHead + (expr*) + TemplateTail
    //   TemplateHead + (expr+) + TemplateMiddle + (expr*) + TemplateTail
    TemplateNoSubstitution,
    TemplateHead,
    TemplateMiddle,
    TemplateTail,

    // ----- Keywords (ECMAScript §12.6.2 ReservedWord) -------------------
    Break, Case, Catch, Class, Const, Continue,
    Debugger, Default, Delete, Do,
    Else, Enum, Export, Extends,
    Finally, For, Function,
    If, Import, In, Instanceof,
    New, Return, Super, Switch,
    This, Throw, Try, Typeof,
    Var, Void, While, With, Yield,

    // ----- Punctuators (§12.8) ------------------------------------------
    LBrace, RBrace, LParen, RParen, LBracket, RBracket,
    Dot, Ellipsis, Semicolon, Comma,
    Lt, Gt, LtEq, GtEq, EqEq, BangEq, EqEqEq, BangEqEq,
    Plus, Minus, Star, Percent, StarStar,
    PlusPlus, MinusMinus,
    LtLt, GtGt, GtGtGt,
    Amp, Pipe, Caret, Tilde, Bang,
    AmpAmp, PipePipe, QuestionQuestion,
    Question, Colon, QuestionDot,
    Eq, PlusEq, MinusEq, StarEq, PercentEq, StarStarEq,
    LtLtEq, GtGtEq, GtGtGtEq,
    AmpEq, PipeEq, CaretEq,
    AmpAmpEq, PipePipeEq, QuestionQuestionEq,
    Arrow,
    Slash, SlashEq,    // ambiguous with regex; parser disambiguates

    // ----- End markers --------------------------------------------------
    EndOfFile,
    Invalid,
}
