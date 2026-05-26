namespace Starling.Js.RegExp;

/// <summary>
/// Pike-VM regex matcher per Russ Cox's "Regular Expression Matching: the
/// Virtual Machine Approach". Linear-time matching for the common subset.
/// Backreferences and lookbehind fall back to a per-thread recursive matcher.
/// </summary>
public sealed class RegexPikeVm
{
    private readonly RegexProgram _prog;
    private readonly RegexFlags _flags;

    public RegexPikeVm(RegexProgram program, RegexFlags flags)
    {
        _prog = program;
        _flags = flags;
    }

    /// <summary>Run the program starting at <paramref name="start"/> against
    /// <paramref name="input"/>. Returns null if no match is found at or after
    /// the start position (or only at the exact start position when
    /// <c>sticky</c> is set).</summary>
    public RegexMatch? Exec(string input, int start)
    {
        bool sticky = (_flags & RegexFlags.Sticky) != 0;
        // Whether the pattern contains backref or lookaround — if so use the
        // slow recursive matcher (handles backreferences correctly).
        bool needSlow = HasBackrefOrLookaround(_prog);

        int maxStart = sticky ? start : input.Length;
        for (var pos = start; pos <= maxStart; pos++)
        {
            RegexMatch? m;
            if (needSlow)
                m = ExecSlow(input, pos);
            else
                m = ExecPike(input, pos);
            if (m is not null) return m;
            if (sticky) return null;
        }
        return null;
    }

    private static bool HasBackrefOrLookaround(RegexProgram p)
    {
        foreach (var i in p.Code)
            if (i.Op == RegexOp.Backref || i.Op == RegexOp.Lookaround) return true;
        return false;
    }

    // ------------------------------------------------------------------
    //                    Pike VM (linear time)
    // ------------------------------------------------------------------
    private RegexMatch? ExecPike(string input, int start)
    {
        var code = _prog.Code;
        int slotCount = (_prog.CaptureCount + 1) * 2;
        var curr = new List<Thread>();
        var next = new List<Thread>();
        var seen = new HashSet<int>();
        // §22.2.2.1 RegExpBuiltinExec initializes every capture to undefined; a
        // capture group that doesn't participate in the match must read back as
        // undefined, not the empty string. The slot array uses -1 to mean
        // "not captured", so it must start all -1 (default int[] is all-zero,
        // which would mis-report non-participating groups as the empty span 0,0).
        var initialSlots = new int[slotCount];
        for (var i = 0; i < slotCount; i++) initialSlots[i] = -1;
        AddThread(curr, seen, new Thread(0, initialSlots), input, start);
        RegexMatch? best = null;
        int pos = start;
        while (true)
        {
            int cp = pos < input.Length ? input[pos] : -1;
            int charLen = 1;
            if (cp >= 0 && char.IsHighSurrogate((char)cp) && pos + 1 < input.Length
                && char.IsLowSurrogate(input[pos + 1])
                && (_flags & (RegexFlags.Unicode | RegexFlags.UnicodeSets)) != 0)
            {
                cp = char.ConvertToUtf32((char)cp, input[pos + 1]);
                charLen = 2;
            }

            seen.Clear();
            for (var t = 0; t < curr.Count; t++)
            {
                var th = curr[t];
                var ins = code[th.Pc];
                bool consumed = false;
                switch (ins.Op)
                {
                    case RegexOp.Match:
                        // Record longest leftmost match; lower-priority threads
                        // at this position are discarded, but next-list keeps
                        // higher-priority continuations queued from earlier in
                        // curr-processing.
                        best = MakeMatchFromSlots(input, start, pos, th.Slots);
                        goto AfterCurr;
                    case RegexOp.Char:
                        if (cp >= 0 && cp == ins.Arg1)
                        {
                            AddThread(next, seen, new Thread(th.Pc + 1, th.Slots), input, pos + charLen);
                            consumed = true;
                        }
                        break;
                    case RegexOp.CharIgnoreCase:
                        if (cp >= 0 && CaseFoldEquals(cp, ins.Arg1))
                        {
                            AddThread(next, seen, new Thread(th.Pc + 1, th.Slots), input, pos + charLen);
                            consumed = true;
                        }
                        break;
                    case RegexOp.CharClass:
                        if (cp >= 0 && _prog.Klasses[ins.Arg1].Contains(cp))
                        {
                            AddThread(next, seen, new Thread(th.Pc + 1, th.Slots), input, pos + charLen);
                            consumed = true;
                        }
                        break;
                    case RegexOp.Any:
                        if (cp >= 0)
                        {
                            AddThread(next, seen, new Thread(th.Pc + 1, th.Slots), input, pos + charLen);
                            consumed = true;
                        }
                        break;
                    case RegexOp.AnyExceptNewline:
                        if (cp >= 0 && !RegexCharClass.IsLineTerminator(cp))
                        {
                            AddThread(next, seen, new Thread(th.Pc + 1, th.Slots), input, pos + charLen);
                            consumed = true;
                        }
                        break;
                }
                if (!consumed) { /* discarded — non-consuming ops were already expanded by AddThread */ }
            }
            AfterCurr:
            // If we already matched at this position and there are no further
            // higher-priority continuations queued in next, stop here.
            if (pos >= input.Length) break;
            pos += charLen;
            (curr, next) = (next, curr);
            next.Clear();
            if (curr.Count == 0) break;
        }
        return best;
    }

    private bool CaseFoldEquals(int a, int b)
    {
        if (a == b) return true;
        if (a > 0xFFFF || b > 0xFFFF) return false;
        var ca = (char)a; var cb = (char)b;
        return char.ToLowerInvariant(ca) == char.ToLowerInvariant(cb)
            || char.ToUpperInvariant(ca) == char.ToUpperInvariant(cb);
    }

    private struct Thread
    {
        public int Pc;
        public int[] Slots;
        public Thread(int pc, int[] slots) { Pc = pc; Slots = slots; }
    }

    private void AddThread(List<Thread> list, HashSet<int> seen, Thread t, string input, int pos)
    {
        // Split's lower-priority branch is parked on a heap-allocated stack
        // instead of via native recursion. A deeply chained regex (e.g. a
        // long run of `(?:|x)` from minified site code) would otherwise blow
        // the native stack — once per epsilon Split — before the JS engine's
        // logical call-stack guard in JsVm could fire.
        Stack<Thread>? deferred = null;
        while (true)
        {
            if (seen.Add(t.Pc))
            {
                var ins = _prog.Code[t.Pc];
                switch (ins.Op)
                {
                    case RegexOp.Jmp:
                        t = new Thread(ins.Arg1, t.Slots);
                        continue;
                    case RegexOp.Split:
                        // Defer Arg2 (lower priority) and continue inline on
                        // Arg1 — preserving leftmost-first / greedy ordering.
                        // The deferred thread gets a slot clone so SaveStart/
                        // SaveEnd mutations on the Arg1 chain don't leak into
                        // it when it's later popped.
                        (deferred ??= new Stack<Thread>())
                            .Push(new Thread(ins.Arg2, (int[])t.Slots.Clone()));
                        t = new Thread(ins.Arg1, t.Slots);
                        continue;
                    case RegexOp.SaveStart:
                        {
                            var slots = (int[])t.Slots.Clone();
                            slots[ins.Arg1 * 2] = pos;
                            t = new Thread(t.Pc + 1, slots);
                            continue;
                        }
                    case RegexOp.SaveEnd:
                        {
                            var slots = (int[])t.Slots.Clone();
                            slots[ins.Arg1 * 2 + 1] = pos;
                            t = new Thread(t.Pc + 1, slots);
                            continue;
                        }
                    case RegexOp.AssertStart:
                        if (pos == 0 || ((_flags & RegexFlags.Multiline) != 0 && pos > 0
                            && RegexCharClass.IsLineTerminator(input[pos - 1])))
                        {
                            t = new Thread(t.Pc + 1, t.Slots);
                            continue;
                        }
                        break;
                    case RegexOp.AssertEnd:
                        if (pos == input.Length || ((_flags & RegexFlags.Multiline) != 0 && pos < input.Length
                            && RegexCharClass.IsLineTerminator(input[pos])))
                        {
                            t = new Thread(t.Pc + 1, t.Slots);
                            continue;
                        }
                        break;
                    case RegexOp.AssertWordBoundary:
                        if (IsWordBoundary(input, pos))
                        {
                            t = new Thread(t.Pc + 1, t.Slots);
                            continue;
                        }
                        break;
                    case RegexOp.AssertNonWordBoundary:
                        if (!IsWordBoundary(input, pos))
                        {
                            t = new Thread(t.Pc + 1, t.Slots);
                            continue;
                        }
                        break;
                    default:
                        list.Add(t);
                        break;
                }
            }
            // Current branch terminated (seen-dedup, assert failure, or
            // list.Add). Pop the next deferred branch; done when empty.
            if (deferred is null || deferred.Count == 0) return;
            t = deferred.Pop();
        }
    }

    private static bool IsWordBoundary(string s, int pos)
    {
        bool before = pos > 0 && RegexCharClass.IsWordChar(s[pos - 1]);
        bool after = pos < s.Length && RegexCharClass.IsWordChar(s[pos]);
        return before != after;
    }

    private static RegexMatch MakeMatchFromSlots(string input, int matchStart, int matchEnd, int[] slots)
    {
        // slots[0]/slots[1] hold group 0 (full match). If unset (rare), fall
        // back to (matchStart, matchEnd).
        var captures = new int[slots.Length];
        for (var i = 0; i < slots.Length; i++) captures[i] = slots[i];
        if (captures.Length >= 2 && (captures[0] < 0 || captures[1] < 0))
        {
            captures[0] = matchStart;
            captures[1] = matchEnd;
        }
        var start = captures.Length >= 2 ? captures[0] : matchStart;
        var end = captures.Length >= 2 ? captures[1] : matchEnd;
        return new RegexMatch(input, start, end, captures);
    }

    // ------------------------------------------------------------------
    //          Slow recursive matcher (backrefs + lookarounds)
    // ------------------------------------------------------------------
    /// <summary>Recursive-descent matcher. Quadratic worst-case but supports
    /// backreferences and lookarounds. Used only when the pattern contains
    /// those constructs.</summary>
    private RegexMatch? ExecSlow(string input, int start)
    {
        int slotCount = (_prog.CaptureCount + 1) * 2;
        var slots = new int[slotCount];
        for (var i = 0; i < slotCount; i++) slots[i] = -1;
        if (TryMatch(_prog, input, start, 0, slots, out var endPos, out var finalSlots))
        {
            return MakeMatchFromSlots(input, start, endPos, finalSlots);
        }
        return null;
    }

    private bool TryMatch(RegexProgram prog, string input, int pos, int pc, int[] slots,
        out int endPos, out int[] outSlots)
    {
        endPos = -1;
        outSlots = slots;
        var code = prog.Code;
        while (pc < code.Count)
        {
            var ins = code[pc];
            switch (ins.Op)
            {
                case RegexOp.Match:
                    endPos = pos;
                    outSlots = slots;
                    return true;
                case RegexOp.Jmp:
                    pc = ins.Arg1;
                    continue;
                case RegexOp.Split:
                    {
                        // Try Arg1 first; if it fails, try Arg2.
                        var save = (int[])slots.Clone();
                        if (TryMatch(prog, input, pos, ins.Arg1, save, out endPos, out outSlots))
                            return true;
                        pc = ins.Arg2;
                        continue;
                    }
                case RegexOp.SaveStart:
                    slots[ins.Arg1 * 2] = pos;
                    pc++;
                    continue;
                case RegexOp.SaveEnd:
                    slots[ins.Arg1 * 2 + 1] = pos;
                    pc++;
                    continue;
                case RegexOp.AssertStart:
                    if (pos == 0 || ((_flags & RegexFlags.Multiline) != 0 && pos > 0
                        && RegexCharClass.IsLineTerminator(input[pos - 1])))
                    {
                        pc++;
                        continue;
                    }
                    return false;
                case RegexOp.AssertEnd:
                    if (pos == input.Length || ((_flags & RegexFlags.Multiline) != 0 && pos < input.Length
                        && RegexCharClass.IsLineTerminator(input[pos])))
                    {
                        pc++;
                        continue;
                    }
                    return false;
                case RegexOp.AssertWordBoundary:
                    if (IsWordBoundary(input, pos)) { pc++; continue; }
                    return false;
                case RegexOp.AssertNonWordBoundary:
                    if (!IsWordBoundary(input, pos)) { pc++; continue; }
                    return false;
                case RegexOp.Char:
                    if (pos < input.Length && input[pos] == ins.Arg1) { pos++; pc++; continue; }
                    return false;
                case RegexOp.CharIgnoreCase:
                    if (pos < input.Length && CaseFoldEquals(input[pos], ins.Arg1)) { pos++; pc++; continue; }
                    return false;
                case RegexOp.CharClass:
                    if (pos < input.Length && prog.Klasses[ins.Arg1].Contains(input[pos]))
                    {
                        pos++; pc++; continue;
                    }
                    return false;
                case RegexOp.Any:
                    if (pos < input.Length) { pos++; pc++; continue; }
                    return false;
                case RegexOp.AnyExceptNewline:
                    if (pos < input.Length && !RegexCharClass.IsLineTerminator(input[pos]))
                    {
                        pos++; pc++; continue;
                    }
                    return false;
                case RegexOp.Backref:
                    {
                        var idx = ins.Arg1;
                        if (idx * 2 + 1 >= slots.Length) return false;
                        var s = slots[idx * 2];
                        var e = slots[idx * 2 + 1];
                        if (s < 0 || e < 0)
                        {
                            // Matches empty string when not yet captured.
                            pc++;
                            continue;
                        }
                        var len = e - s;
                        if (pos + len > input.Length) return false;
                        for (var k = 0; k < len; k++)
                        {
                            var a = input[s + k];
                            var b = input[pos + k];
                            if ((_flags & RegexFlags.IgnoreCase) != 0)
                            {
                                if (!CaseFoldEquals(a, b)) return false;
                            }
                            else if (a != b) return false;
                        }
                        pos += len;
                        pc++;
                        continue;
                    }
                case RegexOp.Lookaround:
                    {
                        var sub = prog.Subs[ins.Arg1];
                        bool negative = (ins.Arg2 & 1) != 0;
                        bool behind = (ins.Arg2 & 2) != 0;
                        bool matched;
                        if (behind)
                        {
                            // Try matches that end at pos. Scan starting positions
                            // from 0 to pos.
                            matched = false;
                            for (var i = 0; i <= pos; i++)
                            {
                                var subSlots = new int[(sub.CaptureCount + 1) * 2];
                                for (var k = 0; k < subSlots.Length; k++) subSlots[k] = -1;
                                if (TryMatch(sub, input, i, 0, subSlots, out var endP, out _) && endP == pos)
                                {
                                    matched = true; break;
                                }
                            }
                        }
                        else
                        {
                            var subSlots = new int[(sub.CaptureCount + 1) * 2];
                            for (var k = 0; k < subSlots.Length; k++) subSlots[k] = -1;
                            matched = TryMatch(sub, input, pos, 0, subSlots, out _, out _);
                        }
                        if (matched != negative) { pc++; continue; }
                        return false;
                    }
                default:
                    return false;
            }
        }
        return false;
    }
}
