using Tessera.Css.Tokenizer;

namespace Tessera.Css.Parser;

public sealed record StyleSheet(
    string Source,
    IReadOnlyList<CssRule> Rules,
    StyleOrigin Origin = StyleOrigin.Author);

public abstract record CssRule;

public sealed record StyleRule(
    IReadOnlyList<CssComponentValue> Prelude,
    IReadOnlyList<CssDeclaration> Declarations,
    IReadOnlyList<CssRule>? NestedRules = null) : CssRule
{
    public IReadOnlyList<CssRule> NestedRulesOrEmpty => NestedRules ?? [];
}

public sealed record AtRule(
    string Name,
    IReadOnlyList<CssComponentValue> Prelude,
    IReadOnlyList<CssRule> Rules,
    IReadOnlyList<CssDeclaration> Declarations) : CssRule;

public sealed record CssDeclaration(
    string Name,
    IReadOnlyList<CssComponentValue> Value,
    bool Important = false);

public abstract record CssComponentValue;

public sealed record CssTokenValue(CssToken Token) : CssComponentValue;

public sealed record CssSimpleBlock(
    CssTokenType StartToken,
    IReadOnlyList<CssComponentValue> Values) : CssComponentValue;

public sealed record CssFunction(
    string Name,
    IReadOnlyList<CssComponentValue> Values) : CssComponentValue;
