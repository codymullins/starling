# 04 — HTML Parsing

## Scope

**In:** Byte-level input stream, encoding sniffing, tokenizer state machine (all states), tree construction (all insertion modes, active formatting elements, adoption agency algorithm), script integration points, error recovery, public seam to DOM.
**Out:** DOM internals ([05_DOM.md](05_DOM.md)), script execution ([09_JS_ENGINE.md](09_JS_ENGINE.md)).

## Spec refs

- [SPEC: HTML §13 Parsing](https://html.spec.whatwg.org/multipage/parsing.html) — the entire algorithm
- [SPEC: HTML §4 Encoding](https://html.spec.whatwg.org/multipage/parsing.html#determining-the-character-encoding)
- [SPEC: Encoding](https://encoding.spec.whatwg.org/) — `utf-8` and friends

The HTML spec is the single source of truth. **Do not invent**. Cite section numbers in code comments.

## Reference implementations to read (not copy)

- `html5gum` (Rust) — clean tokenizer, BSD-licensed. Read for structure.
- `parse5` (JS) — most spec-faithful tree builder. Read insertion-mode handlers.
- `golang.org/x/net/html` — pragmatic Go port. Read for performance choices.

## Project layout

```
src/Tessera.Html/
├── Tessera.Html.csproj
├── IHtmlParser.cs
├── HtmlParser.cs                # façade
├── InputStream/
│   ├── ByteSniffer.cs           # encoding detection
│   ├── PreprocessedStream.cs    # CR/LF/NUL normalization, BOM strip
│   └── CodePointReader.cs       # surrogate-safe Rune reader
├── Tokenizer/
│   ├── HtmlTokenizer.cs         # state machine
│   ├── HtmlToken.cs             # discriminated union
│   ├── CharacterReference.cs    # named entity resolution
│   ├── NamedCharacterReferences.cs   # generated table
│   └── States.cs                # 80+ TokenizerState enum
├── TreeBuilder/
│   ├── TreeBuilder.cs
│   ├── InsertionMode.cs
│   ├── ActiveFormattingElements.cs
│   ├── OpenElementStack.cs
│   ├── AdoptionAgency.cs
│   └── ForeignContent.cs        # MathML / SVG fragment handling
└── Fragment/
    └── FragmentParser.cs        # for innerHTML
```

## Public API

```csharp
namespace Tessera.Html;

public interface IHtmlParser
{
    /// <summary>Push more bytes. Idempotent on isLast=true.</summary>
    ValueTask FeedAsync(ReadOnlyMemory<byte> bytes, bool isLast, CancellationToken ct);

    /// <summary>The Document being built. Mutates as bytes arrive.</summary>
    Document Document { get; }

    /// <summary>Fires when the parser pauses because a script blocks the parser.</summary>
    event EventHandler<ScriptBlockArgs>? ScriptBlocked;

    /// <summary>Signal that the script has finished; resume parsing.</summary>
    ValueTask ResumeAsync(CancellationToken ct);

    /// <summary>True when end-of-file processing is complete.</summary>
    bool IsComplete { get; }
}

public sealed class HtmlParser : IHtmlParser
{
    public HtmlParser(Document document, Url documentUrl, HtmlParserOptions opts);
}

public sealed record HtmlParserOptions(
    bool Scripting = true,
    Encoding? OverrideEncoding = null);

public sealed class FragmentParser
{
    public DocumentFragment Parse(Element context, string html);
}
```

## Input stream

### Encoding detection

Per [SPEC: HTML §13.2.3](https://html.spec.whatwg.org/multipage/parsing.html#determining-the-character-encoding):

1. **User-provided override** (e.g. HTTP `Content-Type` charset). Tentative confidence "certain".
2. **BOM**: `EF BB BF` → utf-8, `FE FF` → utf-16be, `FF FE` → utf-16le. Confidence "certain".
3. **Meta prescan**: scan first 1024 bytes for `<meta charset>` / `<meta http-equiv>`. Confidence "tentative".
4. **Default**: depends on locale; we hardcode `windows-1252` to match Chromium for "any other case" (already the spec default for Western locales).

### Preprocessor

Per [SPEC: HTML §13.2.4](https://html.spec.whatwg.org/multipage/parsing.html#preprocessing-the-input-stream):
- Replace `U+000D` followed by `U+000A` with single `U+000A`.
- Standalone `U+000D` → `U+000A`.
- `U+0000` → `U+FFFD`.
- Strip leading BOM if encoding was inferred from BOM.

### Reader

Provide `Rune`-level reader. **Use `System.Text.Rune` for code points** — handles surrogate pairs correctly. The tokenizer operates on `int codePoint = -1`-or-rune semantics; EOF = -1.

## Tokenizer

State machine. **80-ish states** per spec §13.2.5. Implement each as a method `private void StateDataState(int c)` etc., dispatched via a `delegate*<int, void>[]` array indexed by `TokenizerState`. This is hot — avoid `switch(state)` over 80 cases on every character.

### Token types

```csharp
public abstract record HtmlToken;
public sealed record CharacterToken(int CodePoint)             : HtmlToken;
public sealed record StartTagToken(
    string Name, List<HtmlAttribute> Attributes,
    bool SelfClosing)                                          : HtmlToken;
public sealed record EndTagToken(
    string Name, List<HtmlAttribute> Attributes,
    bool SelfClosing)                                          : HtmlToken;
public sealed record CommentToken(string Data)                 : HtmlToken;
public sealed record DoctypeToken(
    string? Name, string? PublicId, string? SystemId,
    bool ForceQuirks)                                          : HtmlToken;
public sealed record EndOfFileToken                            : HtmlToken;

public sealed record HtmlAttribute(string Name, string Value);
```

Pool `StartTagToken`/`EndTagToken`/`CommentToken` via `ObjectPool<T>` keyed by token type. Names are interned (the set is small: ~150 known tag names).

### State table — checklist

The agent must implement **all** of:

```
Data, RCDATA, RAWTEXT, ScriptData, PLAINTEXT,
TagOpen, EndTagOpen, TagName,
RCDATALessThanSign, RCDATAEndTagOpen, RCDATAEndTagName,
RAWTEXTLessThanSign, RAWTEXTEndTagOpen, RAWTEXTEndTagName,
ScriptDataLessThanSign, ScriptDataEndTagOpen, ScriptDataEndTagName,
ScriptDataEscapeStart, ScriptDataEscapeStartDash,
ScriptDataEscaped, ScriptDataEscapedDash, ScriptDataEscapedDashDash,
ScriptDataEscapedLessThanSign, ScriptDataEscapedEndTagOpen, ScriptDataEscapedEndTagName,
ScriptDataDoubleEscapeStart, ScriptDataDoubleEscaped, ScriptDataDoubleEscapedDash,
ScriptDataDoubleEscapedDashDash, ScriptDataDoubleEscapedLessThanSign, ScriptDataDoubleEscapeEnd,
BeforeAttributeName, AttributeName, AfterAttributeName,
BeforeAttributeValue, AttributeValueDoubleQuoted, AttributeValueSingleQuoted,
AttributeValueUnquoted, AfterAttributeValueQuoted,
SelfClosingStartTag, BogusComment,
MarkupDeclarationOpen, CommentStart, CommentStartDash, Comment,
CommentLessThanSign, CommentLessThanSignBang, CommentLessThanSignBangDash,
CommentLessThanSignBangDashDash, CommentEndDash, CommentEnd, CommentEndBang,
Doctype, BeforeDoctypeName, DoctypeName, AfterDoctypeName,
AfterDoctypePublicKeyword, BeforeDoctypePublicIdentifier,
DoctypePublicIdentifierDoubleQuoted, DoctypePublicIdentifierSingleQuoted,
AfterDoctypePublicIdentifier, BetweenDoctypePublicAndSystemIdentifiers,
AfterDoctypeSystemKeyword, BeforeDoctypeSystemIdentifier,
DoctypeSystemIdentifierDoubleQuoted, DoctypeSystemIdentifierSingleQuoted,
AfterDoctypeSystemIdentifier, BogusDoctype,
CdataSection, CdataSectionBracket, CdataSectionEnd,
CharacterReference, NamedCharacterReference, AmbiguousAmpersand,
NumericCharacterReference, HexadecimalCharacterReferenceStart,
DecimalCharacterReferenceStart, HexadecimalCharacterReference,
DecimalCharacterReference, NumericCharacterReferenceEnd
```

### Named character references

Generate `NamedCharacterReferences.cs` at build time from the spec's [entities.json](https://html.spec.whatwg.org/entities.json). 2231 named references, longest match wins. Implement as a trie. Tool: `tools/gen-entities/Program.cs`.

```csharp
public static class NamedCharacterReferences
{
    /// <summary>Returns count of consumed chars, -1 if no match.</summary>
    public static int Match(ReadOnlySpan<char> input, out int cp1, out int cp2);
}
```

### Parse errors

Tokenizer emits parse errors to a sink. Keep going. Real parse errors are listed in [SPEC: HTML §13.2.2](https://html.spec.whatwg.org/multipage/parsing.html#parse-errors). Implement all ~85 named codes for WPT compliance.

```csharp
public enum HtmlParseError { AbruptClosingOfEmptyComment, AbruptDoctypePublicIdentifier, /* ... */ }
```

## Tree builder

Per [SPEC: HTML §13.2.6](https://html.spec.whatwg.org/multipage/parsing.html#tree-construction). State-machined over **insertion modes**:

```
Initial, BeforeHtml, BeforeHead, InHead, InHeadNoscript,
AfterHead, InBody, Text, InTable, InTableText, InCaption,
InColumnGroup, InTableBody, InRow, InCell, InSelect,
InSelectInTable, InTemplate, AfterBody, InFrameset, AfterFrameset,
AfterAfterBody, AfterAfterFrameset
```

Implement each as a method. Spec is line-by-line literal; implement it literally. Cross-reference section numbers in code comments.

### Data structures

```csharp
public sealed class OpenElementStack
{
    public Element Current { get; }
    public void Push(Element e);
    public Element Pop();
    public bool HasInScope(string tagName);
    public bool HasInListItemScope(string tagName);
    public bool HasInButtonScope(string tagName);
    public bool HasInTableScope(string tagName);
    public bool HasInSelectScope(string tagName);
    public void PopUntilNamed(string tagName);
}

public sealed class ActiveFormattingElements
{
    public void Add(Element e);
    public void AddMarker();
    public void ClearUpToMarker();
    public void ReconstructIfNeeded(TreeBuilder tb);
    // Noah's Ark clause: max 3 identical entries
}
```

### Adoption agency algorithm

Implement [SPEC: HTML §13.2.6.4.7](https://html.spec.whatwg.org/multipage/parsing.html#adoption-agency-algorithm) **literally**. It is gnarly. It handles misnested formatting tags like `<b><i></b></i>`. Step-by-step:

```
1. let subject = current node tagName? if so, pop and return
2. outer loop, up to 8 times
3.   formatting element = topmost <subject> in AFE
4.   ...
```

Test this exhaustively against [html5lib-tests `tests1.dat`](https://github.com/html5lib/html5lib-tests).

### Foreign content

`<svg>` and `<math>` switch the parser into foreign content insertion mode. Token names are case-corrected per [SPEC: HTML §13.2.6.5](https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inforeign). Most attribute names too (e.g. `viewBox`). Generated maps.

## Script integration

Inline `<script>` blocks the parser. Real handling per [SPEC: HTML §13.2.6.4.4 "in body" / `script`](https://html.spec.whatwg.org/multipage/parsing.html#scriptEndTag) and §13.2.9.

```
On end-tag </script>:
  1. let script = currentNode (the SCRIPT element)
  2. pop it from open element stack
  3. set insertion point to current position
  4. parser.PauseTokenizing()
  5. raise ScriptBlocked(script)
  6. consumer evaluates script via Tessera.Js
  7. consumer calls parser.ResumeAsync()
  8. parser resumes from saved insertion point
```

External `<script src>`: pause **before** evaluating; await the fetch.

`async`/`defer` semantics: queue, evaluate post-DOMContentLoaded for `defer`, anytime for `async`. Implementation lives in [10_WEB_APIS.md#script-loading](10_WEB_APIS.md#script-loading); the parser only exposes the hook.

## Incremental parsing

Parser is **push-driven**. The engine feeds bytes as they arrive on the network. The tokenizer is restartable: it always has a known state and an empty look-ahead buffer at chunk boundaries.

### `document.write` — NOT IMPLEMENTED

**OUT-OF-SCOPE-V1.** Modern target sites (google.com, claude.ai) do not call `document.write`. The spec-mandated re-entry logic (inject characters at the current insertion point, tokenize before subsequent network bytes) is meaningful complexity in the parser.

Bindings expose `Document.prototype.write` and `Document.prototype.writeln` as functions that **throw `NotSupportedError`**. If a target site we care about trips this, we add it back; until then it's dead weight.

### Speculative parsing

OUT-OF-SCOPE-V1. (Chrome does it for `<link rel=preload>` discovery during script blocks.)

## Encoding switch mid-parse

If the meta prescan was wrong and the tree builder sees a `<meta charset>` indicating a different encoding (within first 1024 bytes), spec says "change the encoding" — re-decode pending bytes. In v1, implement the "abort and restart" branch: clear DOM, reset tokenizer, re-feed from byte 0 in new encoding. Acceptable.

After tree construction has progressed past head, ignore further `<meta charset>` directives.

## DOCTYPE handling

Doctype switches the `Document.mode`:
- `<!DOCTYPE html>` → no-quirks.
- Known quirky doctypes (long list in spec §13.2.6.2) → quirks.
- Anything else → limited-quirks.

Quirks mode tweaks CSS in [06_CSS.md#quirks](06_CSS.md#quirks-mode) and a handful of DOM/HTML quirks (e.g. `<table>` line-height).

## Public failure modes

- Empty input → emit `Document` with `<html><head></head><body></body></html>`.
- Truncated input mid-tag → close all open elements at EOF.
- All other failures are recoverable per spec.

## Performance budget

For a 1MB HTML document (e.g. Google search results):
- Tokenize: ≤ 30ms cold, ≤ 10ms warm on a 2024-era laptop.
- Tree build: ≤ 20ms.
- Total parse ≤ 50ms.

Hot-path rules:
- No string allocations per character. Use `Span<char>` buffers and `string.Create`.
- Tag name comparison: lowercase ASCII fast path, then compare ordinal.
- Attribute storage: small list (most elements have ≤ 4 attrs); use a 4-slot inline struct.

## Acceptance Tests

- [ ] All html5lib `tokenizer/*.test` JSON cases pass. URL: <https://github.com/html5lib/html5lib-tests>.
- [ ] All html5lib `tree-construction/*.dat` cases pass.
- [ ] WPT subset `html/syntax/parsing/**` passes ≥ 99%.
- [ ] Parser accepts `https://example.com` content streamed in 17-byte chunks and produces the same DOM as feeding it whole.
- [ ] `document.write('...')` from JS throws `NotSupportedError` (does not crash the engine).
- [ ] DOCTYPE switching to quirks mode is visible on `Document.Mode`.
- [ ] Parser pauses on `<script>` and resumes after `ResumeAsync()`.
- [ ] Memory: 1MB doc → ≤ 5MB working set after parse.
