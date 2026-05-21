---
id: "wp:M3-04g-js-async-generators"
parent: "wp:M3-04-js-vm"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody-asyncgen"
claimed_at: "2026-05-21T01:01:13Z"
branch: "main"
depends_on:
  - "wp:M3-04b-js-functions"
blocks: []
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#async"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-04-js-vm"
---

# wp:M3-04g — JS: async generators (`async function*`, `for await…of`)

## Goal
Replace the async-generator stub with a real implementation: `async function*`
bodies may interleave `yield` and `await`; `.next()/.return()/.throw()` each
return a Promise of `{ value, done }`; `for await (… of asyncGen)` works. Build
on the existing generator suspension core and the async/await drive machinery —
both already exist.

## Inputs
- Sync generators: `JsVm.StartGeneratorBody` (`src/Starling.Js/Runtime/JsVm.cs`
  ~1844–1872), `Runtime/JsGenerator.cs`, `Runtime/SuspendedFrame.cs`,
  `Intrinsics/GeneratorIntrinsics.cs` (`GeneratorNext/Return/Throw`).
- Async functions: `JsVm.StartAsyncBody` (~1877–1908), `DriveAsync` (~1914–1928),
  `ScheduleAwait` (~1930–1967), `SettleAsync` (~1969–1978),
  `Runtime/JsGenerator.cs` `JsAsyncFunctionState`, `Intrinsics/PromiseCtor.cs`,
  `Runtime/MicrotaskQueue.cs`.
- Shared `Opcode.Suspend [u8 kind]` (`Bytecode/Opcode.cs` ~252–259): kind 0 =
  yield, kind 1 = await. The compiler already emits the right kinds inside
  `async function*` bodies and already computes
  `JsFunctionKind.AsyncGenerator` — parsing + compilation are DONE.
- The stub to replace: `JsVm.StartAsyncGeneratorBody` (~1980–1998) and
  `GeneratorIntrinsics.AsyncGeneratorNext` (~126–143, "Minimal … Documented
  gap"). `JsRealm.AsyncGeneratorPrototype` / `AsyncIteratorPrototype` exist.

## Scope / where to work
- `src/Starling.Js/Runtime/JsVm.cs`:
  - Real `StartAsyncGeneratorBody`: create a dedicated async-generator object
    (worker frame via `SuspendedFrame`, started lazily on first `.next()`), do
    NOT delegate to `StartGeneratorBody`.
  - A `DriveAsyncGenerator`-style method that resumes the frame and, at each
    suspension, distinguishes kind 0 (yield → resolve the pending `.next()`
    Promise with `{value, done:false}`) from kind 1 (await → wrap in a Promise,
    `.then` back into the frame via the `ScheduleAwait` pattern, then keep
    driving). On normal return, settle with `{value, done:true}`; on throw,
    reject.
  - The `Suspend` handler (~1178–1225) currently rejects `await` unless in an
    async context — make sure async-generator frames are accepted for BOTH
    kinds. Reuse `ScheduleAwait`/microtask wiring; refactor those helpers if
    needed but keep plain async/await behavior identical.
- `src/Starling.Js/Runtime/JsGenerator.cs`: add a `JsAsyncGenerator` state type
  (or extend existing state) tracking the frame, a request queue for
  serialized `.next()/.return()/.throw()` calls, and started/done flags. Async
  generators must queue concurrent calls per spec (AsyncGeneratorEnqueue).
- `src/Starling.Js/Intrinsics/GeneratorIntrinsics.cs`:
  - Real `AsyncGeneratorNext` (drive + return Promise of `{value,done}`).
  - Add `AsyncGeneratorReturn` and `AsyncGeneratorThrow` on
    `AsyncGeneratorPrototype` (currently missing), each returning a Promise.
  - Ensure `[Symbol.asyncIterator]` returns self and the prototype chains to
    `AsyncIteratorPrototype`.
- Verify `for await…of` lowering drives the async-iterator protocol (await each
  step result). If the compiler doesn't yet emit `for await`, implement the
  minimal lowering needed for the acceptance tests; otherwise just test it.

## Outputs
- `async function*` with interleaved `yield`/`await`, `for await…of`, and
  `.return()/.throw()` all behave per spec.

## Acceptance
- New tests in a dedicated file, e.g.
  `tests/Starling.Js.Tests/AsyncGeneratorTests.cs`:
  - yields collected via `for await…of` in order.
  - `await` inside the body actually suspends on a Promise and resumes with the
    resolved value (use a resolved Promise / microtask; assert ordering).
  - `.next()` returns a Promise resolving to `{value, done}`; final `.next()`
    after exhaustion gives `{value:undefined, done:true}`.
  - `.return(v)` early-terminates and runs `finally` blocks.
  - `.throw(e)` injects an exception observable by a `try/catch` in the body.
  - rejected awaited Promise surfaces as a thrown error in the body.
- `dotnet build src/Starling.Js/Starling.Js.csproj -c Debug` green.
- `dotnet test tests/Starling.Js.Tests/Starling.Js.Tests.csproj -c Debug` green.

## Notes
- DO NOT touch any file under `tasks/` — the orchestrator owns task bookkeeping.
- Put new tests in a NEW test file.
- You share `src/Starling.Js/Runtime/JsVm.cs` with a concurrent computed-class
  WP, but in a different region (async/suspend machinery vs class opcodes). Keep
  your additions in the async region (~1844–1998) and the `Suspend` handler to
  minimize merge friction.
- Tests must be deterministic: drive the microtask queue (`MicrotaskQueue.DrainAll`
  or whatever the existing async tests use) rather than relying on wall-clock.

## Handoff log
- 2026-05-21T01:01:13Z — created + claimed for agent-claude-cody-asyncgen (orchestrated Wave 1)
