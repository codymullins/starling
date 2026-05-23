namespace Starling.Js.RegExp;

/// <summary>
/// ES2024 §22.2.1 regex pattern parser. Source string → AST. Throws
/// <see cref="RegexSyntaxException"/> with a message suitable for surfacing as
/// a JS SyntaxError on any grammar violation.
/// </summary>
public sealed class RegexParser
{
    private readonly string _src;
    private readonly RegexFlags _flags;
    private int _i;
    private int _captureCount;
    public int CaptureCount => _captureCount;
    public Dictionary<string, int> NamedCaptures { get; } = new();

    public RegexParser(string src, RegexFlags flags)
    {
        _src = src ?? string.Empty;
        _flags = flags;
    }

    public RegexNode Parse()
    {
        // First pass — count capture groups so backreferences can validate.
        // Lightweight scan: count "(" that aren't "(?…".
        _captureCount = CountCaptures(_src);
        var node = ParseAlternation();
        if (_i != _src.Length) throw new RegexSyntaxException($"Unexpected character at index {_i}");
        return node;
    }

    private static int CountCaptures(string src)
    {
        var n = 0;
        for (var i = 0; i < src.Length; i++)
        {
            var c = src[i];
            if (c == '\\') { i++; continue; }
            if (c == '[')
            {
                while (++i < src.Length && src[i] != ']')
                {
                    if (src[i] == '\\') i++;
                }
                continue;
            }
            if (c == '(')
            {
                if (i + 1 < src.Length && src[i + 1] == '?')
                {
                    if (i + 2 < src.Length && src[i + 2] == '<')
                    {
                        if (i + 3 < src.Length && (src[i + 3] == '=' || src[i + 3] == '!'))
                            continue; // lookbehind, not a capture
                        // named capture
                        n++;
                    }
                    continue;
                }
                n++;
            }
        }
        return n;
    }

    private RegexNode ParseAlternation()
    {
        var alts = new List<RegexNode> { ParseSequence() };
        while (_i < _src.Length && _src[_i] == '|')
        {
            _i++;
            alts.Add(ParseSequence());
        }
        return alts.Count == 1 ? alts[0] : new AlternationNode(alts);
    }

    private RegexNode ParseSequence()
    {
        var items = new List<RegexNode>();
        while (_i < _src.Length)
        {
            var c = _src[_i];
            if (c == '|' || c == ')') break;
            var atom = ParseAtom();
            atom = MaybeQuantify(atom);
            items.Add(atom);
        }
        if (items.Count == 0) return new EmptyNode();
        if (items.Count == 1) return items[0];
        return new SequenceNode(items);
    }

    private RegexNode MaybeQuantify(RegexNode atom)
    {
        if (_i >= _src.Length) return atom;
        var c = _src[_i];
        int min, max;
        switch (c)
        {
            case '*': _i++; min = 0; max = -1; break;
            case '+': _i++; min = 1; max = -1; break;
            case '?': _i++; min = 0; max = 1; break;
            case '{':
                {
                    var save = _i;
                    if (!TryParseBrace(out min, out max))
                    {
                        _i = save;
                        return atom;
                    }
                    break;
                }
            default: return atom;
        }
        bool greedy = true;
        if (_i < _src.Length && _src[_i] == '?')
        {
            greedy = false;
            _i++;
        }
        return new QuantifierNode(atom, min, max, greedy);
    }

    private bool TryParseBrace(out int min, out int max)
    {
        min = 0; max = 0;
        var i = _i;
        if (i >= _src.Length || _src[i] != '{') return false;
        i++;
        var start = i;
        while (i < _src.Length && _src[i] >= '0' && _src[i] <= '9') i++;
        if (i == start) return false;
        if (!int.TryParse(_src[start..i], out min)) return false;
        if (i < _src.Length && _src[i] == '}')
        {
            max = min;
            _i = i + 1;
            return true;
        }
        if (i < _src.Length && _src[i] == ',')
        {
            i++;
            var maxStart = i;
            while (i < _src.Length && _src[i] >= '0' && _src[i] <= '9') i++;
            if (i < _src.Length && _src[i] == '}')
            {
                if (maxStart == i) max = -1;
                else if (!int.TryParse(_src[maxStart..i], out max)) return false;
                if (max != -1 && max < min)
                    throw new RegexSyntaxException("Numbers out of order in {} quantifier");
                _i = i + 1;
                return true;
            }
        }
        return false;
    }

    private RegexNode ParseAtom()
    {
        var c = _src[_i];
        switch (c)
        {
            case '.':
                _i++;
                return new AnyNode((_flags & RegexFlags.DotAll) != 0);
            case '^':
                _i++;
                return new AnchorNode(AnchorKind.StartOfInput);
            case '$':
                _i++;
                return new AnchorNode(AnchorKind.EndOfInput);
            case '(':
                return ParseGroup();
            case '[':
                return ParseClass();
            case '\\':
                return ParseEscape();
            case '*': case '+': case '?': case '|':
                throw new RegexSyntaxException($"Unexpected '{c}' at index {_i}");
            case '{': case '}': case ']':
                // Annex B §B.1.2: with neither the `u` nor `v` flag, `{` `}` `]`
                // that don't form a valid quantifier / close a class are
                // ExtendedPatternCharacters — i.e. ordinary literals. (`a{`, a
                // stray `}`, an unmatched `]`.) Real-world bundles, including
                // google.com's, depend on this. Under Unicode/UnicodeSets the
                // strict grammar applies and they remain a SyntaxError.
                if ((_flags & (RegexFlags.Unicode | RegexFlags.UnicodeSets)) != 0)
                    throw new RegexSyntaxException($"Unexpected '{c}' at index {_i}");
                goto default;
            default:
                {
                    _i++;
                    int cp = c;
                    if (char.IsHighSurrogate(c) && _i < _src.Length && char.IsLowSurrogate(_src[_i])
                        && (_flags & (RegexFlags.Unicode | RegexFlags.UnicodeSets)) != 0)
                    {
                        cp = char.ConvertToUtf32(c, _src[_i]);
                        _i++;
                    }
                    return new LiteralNode(cp);
                }
        }
    }

    private RegexNode ParseGroup()
    {
        // We're at '('. Consume it.
        _i++;
        int? capture = null;
        string? name = null;
        bool isLookaround = false;
        bool lookBehind = false;
        bool negative = false;
        if (_i < _src.Length && _src[_i] == '?')
        {
            _i++;
            if (_i >= _src.Length) throw new RegexSyntaxException("Unterminated group");
            var ch = _src[_i];
            switch (ch)
            {
                case ':': _i++; break;
                case '=': _i++; isLookaround = true; lookBehind = false; negative = false; break;
                case '!': _i++; isLookaround = true; lookBehind = false; negative = true; break;
                case '<':
                    {
                        _i++;
                        if (_i < _src.Length && (_src[_i] == '=' || _src[_i] == '!'))
                        {
                            isLookaround = true;
                            lookBehind = true;
                            negative = _src[_i] == '!';
                            _i++;
                        }
                        else
                        {
                            // Named capture
                            var nameStart = _i;
                            while (_i < _src.Length && _src[_i] != '>') _i++;
                            if (_i >= _src.Length) throw new RegexSyntaxException("Unterminated named group");
                            name = _src[nameStart.._i];
                            _i++; // consume '>'
                            capture = ++_currentCapture;
                            NamedCaptures[name] = capture.Value;
                        }
                        break;
                    }
                default:
                    throw new RegexSyntaxException($"Invalid group qualifier (?{ch} at index {_i})");
            }
        }
        else
        {
            capture = ++_currentCapture;
        }
        var inner = ParseAlternation();
        if (_i >= _src.Length || _src[_i] != ')') throw new RegexSyntaxException("Unterminated group");
        _i++;
        if (isLookaround) return new LookaroundNode(lookBehind, negative, inner);
        return new GroupNode(capture, name, inner);
    }

    private int _currentCapture;

    private CharClassNode ParseClass()
    {
        _i++; // '['
        bool negated = false;
        if (_i < _src.Length && _src[_i] == '^')
        {
            negated = true; _i++;
        }
        // v-flag set ops not implemented; reject syntactically.
        var ranges = new List<(int, int)>();
        var nestedKlasses = new List<RegexCharClass>();
        while (_i < _src.Length && _src[_i] != ']')
        {
            if ((_flags & RegexFlags.UnicodeSets) != 0 && _src[_i] == '[')
                throw new RegexSyntaxException("v-flag character class set operations are not supported");
            if (_i + 1 < _src.Length && _src[_i] == '&' && _src[_i + 1] == '&')
                throw new RegexSyntaxException("v-flag character class set operations are not supported");

            var lo = ParseClassAtom(ranges, nestedKlasses);
            if (_i + 1 < _src.Length && _src[_i] == '-' && _src[_i + 1] != ']')
            {
                _i++; // '-'
                var hi = ParseClassAtom(ranges, nestedKlasses);
                if (lo.HasValue && hi.HasValue)
                {
                    if (hi.Value < lo.Value)
                        throw new RegexSyntaxException("Range out of order in character class");
                    ranges.Add((lo.Value, hi.Value));
                }
                else
                {
                    // Treat as literal '-' between escapes that produced ranges already
                    if (lo.HasValue) ranges.Add((lo.Value, lo.Value));
                    ranges.Add(('-', '-'));
                    if (hi.HasValue) ranges.Add((hi.Value, hi.Value));
                }
            }
            else if (lo.HasValue)
            {
                ranges.Add((lo.Value, lo.Value));
            }
        }
        if (_i >= _src.Length) throw new RegexSyntaxException("Unterminated character class");
        _i++; // ']'
        // Merge nested classes (predefined) into ranges. If any of them is
        // negated independently, we expand it inversely. Simpler approach:
        // merge their ranges only if not negated; if negated, expand to the
        // complement up to U+FFFF.
        foreach (var nested in nestedKlasses)
        {
            for (var cp = 0; cp <= 0xFFFF; cp++)
                if (nested.Contains(cp)) ranges.Add((cp, cp));
        }
        var caseInsensitive = (_flags & RegexFlags.IgnoreCase) != 0;
        return new CharClassNode(new RegexCharClass(ranges, negated, caseInsensitive));
    }

    private int? ParseClassAtom(List<(int, int)> ranges, List<RegexCharClass> nested)
    {
        if (_src[_i] == '\\')
        {
            // Escape inside class
            _i++;
            if (_i >= _src.Length) throw new RegexSyntaxException("Trailing backslash in class");
            var esc = _src[_i];
            switch (esc)
            {
                case 'd': _i++; foreach (var r in RegexCharClass.Digits()) ranges.Add(r); return null;
                case 'D': _i++; AddNegatedRanges(ranges, RegexCharClass.Digits()); return null;
                case 'w': _i++; foreach (var r in RegexCharClass.Word()) ranges.Add(r); return null;
                case 'W': _i++; AddNegatedRanges(ranges, RegexCharClass.Word()); return null;
                case 's': _i++; foreach (var r in RegexCharClass.Whitespace()) ranges.Add(r); return null;
                case 'S': _i++; AddNegatedRanges(ranges, RegexCharClass.Whitespace()); return null;
                case 'p': case 'P':
                    _i++;
                    nested.Add(ParsePropertyEscape(esc == 'P'));
                    return null;
                case 'n': _i++; return '\n';
                case 'r': _i++; return '\r';
                case 't': _i++; return '\t';
                case 'v': _i++; return '\v';
                case 'f': _i++; return '\f';
                case '0': _i++; return 0;
                case 'b': _i++; return 0x08; // backspace in class
                case 'x': _i++; return ParseHex(2);
                case 'u': _i++; return ParseUnicodeEscape();
                case 'c': _i++; return ParseControlChar();
                default:
                    _i++;
                    return esc;
            }
        }
        else
        {
            int cp = _src[_i];
            _i++;
            return cp;
        }
    }

    private static void AddNegatedRanges(List<(int, int)> dest, List<(int, int)> src)
    {
        // Add the complement of src ranges to dest. Limit to BMP.
        src.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        int cursor = 0;
        foreach (var (lo, hi) in src)
        {
            if (lo > cursor) dest.Add((cursor, lo - 1));
            cursor = hi + 1;
        }
        if (cursor <= 0xFFFF) dest.Add((cursor, 0xFFFF));
    }

    /// <summary>Caller must have already consumed the leading 'p'/'P'; we
    /// expect <c>_i</c> to point at the opening brace.</summary>
    private RegexCharClass ParsePropertyEscape(bool negated)
    {
        if (_i >= _src.Length || _src[_i] != '{')
            throw new RegexSyntaxException("Invalid \\p escape: missing '{'");
        _i++;
        var start = _i;
        while (_i < _src.Length && _src[_i] != '}') _i++;
        if (_i >= _src.Length) throw new RegexSyntaxException("Unterminated \\p{ escape");
        var raw = _src[start.._i];
        _i++;
        // Accept "Property=Value" or just "Value"
        var name = raw;
        var eq = raw.IndexOf('=');
        if (eq >= 0) name = raw[(eq + 1)..];
        if (!RegexCharClass.SupportedProperties.Contains(name))
            throw new RegexSyntaxException($"Unsupported Unicode property: {name}");
        // Build a range table by scanning BMP.
        var ranges = new List<(int, int)>();
        int? rangeStart = null;
        for (var cp = 0; cp <= 0xFFFF; cp++)
        {
            if (RegexCharClass.MatchesProperty(cp, name))
            {
                rangeStart ??= cp;
            }
            else if (rangeStart.HasValue)
            {
                ranges.Add((rangeStart.Value, cp - 1));
                rangeStart = null;
            }
        }
        if (rangeStart.HasValue) ranges.Add((rangeStart.Value, 0xFFFF));
        var caseInsensitive = (_flags & RegexFlags.IgnoreCase) != 0;
        return new RegexCharClass(ranges, negated, caseInsensitive);
    }

    private RegexNode ParseEscape()
    {
        _i++; // consume '\'
        if (_i >= _src.Length) throw new RegexSyntaxException("Trailing backslash");
        var c = _src[_i];
        switch (c)
        {
            case 'b': _i++; return new AnchorNode(AnchorKind.WordBoundary);
            case 'B': _i++; return new AnchorNode(AnchorKind.NonWordBoundary);
            case 'd': _i++; return new CharClassNode(new RegexCharClass(RegexCharClass.Digits(), false, false));
            case 'D': _i++; return new CharClassNode(new RegexCharClass(RegexCharClass.Digits(), true, false));
            case 'w': _i++; return new CharClassNode(new RegexCharClass(RegexCharClass.Word(), false, false));
            case 'W': _i++; return new CharClassNode(new RegexCharClass(RegexCharClass.Word(), true, false));
            case 's': _i++; return new CharClassNode(new RegexCharClass(RegexCharClass.Whitespace(), false, false));
            case 'S': _i++; return new CharClassNode(new RegexCharClass(RegexCharClass.Whitespace(), true, false));
            case 'p': case 'P':
                {
                    var negated = c == 'P';
                    _i++;
                    var klass = ParsePropertyEscape(negated);
                    return new CharClassNode(klass);
                }
            case 'n': _i++; return new LiteralNode('\n');
            case 'r': _i++; return new LiteralNode('\r');
            case 't': _i++; return new LiteralNode('\t');
            case 'v': _i++; return new LiteralNode('\v');
            case 'f': _i++; return new LiteralNode('\f');
            case '0':
                _i++;
                if (_i < _src.Length && _src[_i] >= '0' && _src[_i] <= '9')
                    throw new RegexSyntaxException("\\0 may not be followed by another digit");
                return new LiteralNode(0);
            case 'x': _i++; return new LiteralNode(ParseHex(2));
            case 'u': _i++; return new LiteralNode(ParseUnicodeEscape());
            case 'c': _i++; return new LiteralNode(ParseControlChar());
            case 'k':
                {
                    _i++;
                    if (_i >= _src.Length || _src[_i] != '<')
                        throw new RegexSyntaxException("\\k must be followed by <name>");
                    _i++;
                    var nameStart = _i;
                    while (_i < _src.Length && _src[_i] != '>') _i++;
                    if (_i >= _src.Length) throw new RegexSyntaxException("Unterminated \\k<name>");
                    var name = _src[nameStart.._i];
                    _i++; // '>'
                    return new NamedBackrefNode(name);
                }
            default:
                if (c >= '1' && c <= '9')
                {
                    var start = _i;
                    while (_i < _src.Length && _src[_i] >= '0' && _src[_i] <= '9') _i++;
                    var n = int.Parse(_src[start.._i]);
                    return new BackrefNode(n);
                }
                _i++;
                return new LiteralNode(c);
        }
    }

    private int ParseHex(int digits)
    {
        if (_i + digits > _src.Length) throw new RegexSyntaxException("Invalid hex escape");
        var hex = _src.Substring(_i, digits);
        if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v))
            throw new RegexSyntaxException("Invalid hex escape");
        _i += digits;
        return v;
    }

    private int ParseUnicodeEscape()
    {
        // \u{HHHH...} (u flag) or \uHHHH
        if (_i < _src.Length && _src[_i] == '{')
        {
            _i++;
            var start = _i;
            while (_i < _src.Length && _src[_i] != '}') _i++;
            if (_i >= _src.Length) throw new RegexSyntaxException("Unterminated \\u{ escape");
            var hex = _src[start.._i];
            if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v))
                throw new RegexSyntaxException("Invalid \\u{ escape");
            _i++; // '}'
            return v;
        }
        return ParseHex(4);
    }

    private int ParseControlChar()
    {
        if (_i >= _src.Length) throw new RegexSyntaxException("Invalid \\c escape");
        var c = _src[_i];
        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
        {
            _i++;
            return c % 32;
        }
        throw new RegexSyntaxException("Invalid \\c escape");
    }
}

public sealed class RegexSyntaxException : System.Exception
{
    public RegexSyntaxException() { }
    public RegexSyntaxException(string message) : base(message) { }
    public RegexSyntaxException(string message, System.Exception innerException) : base(message, innerException) { }
}
