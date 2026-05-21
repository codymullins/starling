namespace Starling.Js.Runtime;

/// <summary>
/// B1b-2c — runtime handle for a generator / async function body suspended
/// at a <c>yield</c> or <c>await</c> point. Backed by a dedicated worker
/// thread that owns the VM frame's locals + eval stack: when JS code on the
/// main thread calls <c>.next()</c> (generator) or the awaited promise
/// settles (async), the main thread signals the worker to resume and waits
/// for the next suspension or completion.
/// </summary>
/// <remarks>
/// <para>
/// The handoff is synchronous: only one thread runs JS at a time, so the
/// usual single-threaded VM invariants are preserved. The worker thread
/// blocks on <see cref="_resume"/> until the main thread sets it; the main
/// thread blocks on <see cref="_yield"/> until the worker yields. There is
/// no lock — the events provide mutual exclusion.
/// </para>
/// <para>
/// Chosen for implementation simplicity over CPS rewriting. Performance
/// cost: one thread per concurrently-suspended generator/async, plus the
/// signal cost on each yield. Acceptable for the M3 surface.
/// </para>
/// </remarks>
public sealed class SuspendedFrame
{
    private readonly ManualResetEventSlim _resume = new(initialState: false);
    private readonly ManualResetEventSlim _yield = new(initialState: false);
    private Thread? _worker;

    /// <summary>Value pushed by the caller when resuming
    /// (<c>.next(v)</c> for generators, the resolved value for async).
    /// The worker pops this onto its eval stack after Suspend returns.</summary>
    public JsValue ResumeValue { get; set; } = JsValue.Undefined;

    /// <summary>True when the caller wants to inject a throw at the
    /// suspension point — used by generator <c>.throw(e)</c> and by
    /// async <c>await</c> when the awaited promise rejects.</summary>
    public bool ResumeWithThrow { get; set; }

    /// <summary>True when the caller wants to inject a Return completion
    /// at the suspension point — used by generator <c>.return(v)</c> so
    /// the worker walks any enclosing <c>finally</c> blocks before
    /// completing with <see cref="ResumeValue"/> as the return value.
    /// Mutually exclusive with <see cref="ResumeWithThrow"/>.</summary>
    public bool ResumeWithReturn { get; set; }

    /// <summary>Value yielded by the worker (the operand of <c>yield expr</c>
    /// or <c>await expr</c>). For yield it becomes the result-object's
    /// <c>value</c>; for await it's the promise/value to settle on.</summary>
    public JsValue YieldedValue { get; set; } = JsValue.Undefined;

    /// <summary>Why the worker last suspended: <c>0</c> = <c>yield</c>,
    /// <c>1</c> = <c>await</c>. Set by the <see cref="Starling.Js.Bytecode.Opcode.Suspend"/>
    /// handler before it hands off, so the async-generator driver can tell a
    /// genuine yield (settle the pending request with <c>{value, done:false}</c>)
    /// apart from an internal await (resume after the awaited promise settles,
    /// without producing a result for the caller). Sync generators and plain
    /// async functions ignore this field.</summary>
    public int SuspendKind { get; set; }

    /// <summary>True once the worker thread has finished executing the
    /// function body (normally via <c>return</c> or fall-off).</summary>
    public bool Completed { get; private set; }

    /// <summary>The function's return value when <see cref="Completed"/>
    /// becomes true normally. Undefined for explicit-return-less generators.</summary>
    public JsValue ReturnValue { get; private set; } = JsValue.Undefined;

    /// <summary>True if the worker thread threw an uncaught JsThrow. The
    /// thrown value lives in <see cref="ReturnValue"/>.</summary>
    public bool ThrewUncaught { get; private set; }

    /// <summary>The active VM. Stashed so the worker can publish itself as
    /// the realm's <c>ActiveVm</c> before starting bytecode execution.</summary>
    public JsVm Vm { get; }

    public SuspendedFrame(JsVm vm)
    {
        Vm = vm ?? throw new System.ArgumentNullException(nameof(vm));
    }

    /// <summary>Spawn the worker thread that will execute <paramref name="body"/>.
    /// The worker blocks immediately on <see cref="_resume"/>; it only starts
    /// running when the caller invokes <see cref="Resume"/>. The body must
    /// drive the VM's <c>RunInner</c> with this frame as the active
    /// suspension target, so any <see cref="Starling.Js.Bytecode.Opcode.Suspend"/>
    /// dispatches to <see cref="WorkerYield"/>.</summary>
    public void Start(System.Action body)
    {
        if (_worker is not null)
            throw new System.InvalidOperationException("SuspendedFrame already started");
        _worker = new Thread(() =>
        {
            // Wait for first resume signal before doing anything.
            _resume.Wait();
            _resume.Reset();
            try
            {
                body();
            }
            catch (JsThrow ex)
            {
                ThrewUncaught = true;
                ReturnValue = ex.Value;
            }
            catch (System.Exception ex)
            {
                ThrewUncaught = true;
                ReturnValue = JsValue.String("internal VM error: " + ex.Message);
            }
            finally
            {
                Completed = true;
                _yield.Set();
            }
        })
        {
            IsBackground = true,
            Name = "JsSuspendedFrame",
        };
        _worker.Start();
    }

    /// <summary>Worker-side primitive: pause and hand
    /// <paramref name="yielded"/> to the caller. Blocks until
    /// <see cref="Resume"/> wakes us. Returns the resume-value pushed by
    /// the caller. If the caller asked us to throw, the caller side of
    /// Resume injects the throw into the VM via the Suspend opcode handler.
    /// </summary>
    public JsValue WorkerYield(JsValue yielded)
    {
        YieldedValue = yielded;
        _yield.Set();
        _resume.Wait();
        _resume.Reset();
        return ResumeValue;
    }

    /// <summary>Internal — worker calls this just before exiting to publish
    /// the return value (the "completion" the next caller-side
    /// <see cref="Resume"/> sees).</summary>
    internal void SetReturnValue(JsValue v) => ReturnValue = v;

    /// <summary>Internal — worker publishes that its body threw uncaught.</summary>
    internal void SetThrew(JsValue v)
    {
        ThrewUncaught = true;
        ReturnValue = v;
    }

    /// <summary>Caller-side primitive: signal the worker to start (or
    /// continue) running, then block until the worker either yields or
    /// completes. After this returns, callers should consult
    /// <see cref="Completed"/> and either <see cref="YieldedValue"/> (still
    /// running) or <see cref="ReturnValue"/> (done).</summary>
    public void Resume(JsValue value, bool withThrow = false, bool withReturn = false)
    {
        if (Completed) return;
        ResumeValue = value;
        ResumeWithThrow = withThrow;
        ResumeWithReturn = withReturn;
        _resume.Set();
        _yield.Wait();
        _yield.Reset();
    }
}
