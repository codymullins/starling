---
id: wp:M3-31-web-crypto-getrandomvalues
status: complete
owner: agent-claude-cody
depends_on: []
---

# wp:M3-31 — Web Crypto minimal surface (getRandomValues + randomUUID)

## Goal

Cloudflare Insights (`static.cloudflareinsights.com/beacon.min.js`) uses the
`uuid` library which calls `crypto.getRandomValues()`. Without a `crypto`
global, every page that loads CF Insights throws:

```
Error: crypto.getRandomValues() not supported.
```

Implement the two methods all modern random-value consumers need and expose
`crypto` on the realm global.

## Scope

- `crypto.getRandomValues(typedArray)` — fills an integer TypedArray in place,
  returns the same object. Rejects Float32/Float64 (TypeError). Throws a
  RangeError for byteLength > 65536 (modelling the spec QuotaExceededError
  since DOMException is not yet implemented). Backed by
  `System.Security.Cryptography.RandomNumberGenerator.Fill`.
- `crypto.randomUUID()` — returns an RFC 4122 v4 UUID string
  (`xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx`). Built from 16 random bytes with
  version and variant bits set per spec.
- `crypto` exposed as a data property on the realm global (writable,
  configurable), wired into `WindowBinding.Install` step 9.
- `crypto.subtle` intentionally left undefined (out of scope — large
  independent subsystem).

## Out of scope

`SubtleCrypto` (digest / sign / encrypt / key derivation) — left for a future
WP when needed by a real site.

## Files changed

- `src/Starling.Bindings/CryptoBinding.cs` — new; the binding implementation.
- `src/Starling.Bindings/WindowBinding.cs` — step 9 wires in
  `CryptoBinding.Install`.
- `tests/Starling.Bindings.Tests/CryptoBindingTests.cs` — 15 new tests.
- `tasks/M3/wp-M3-31-web-crypto-getrandomvalues.md` — this file.
- `tasks/INDEX.md` — INDEX row added.

## Tests

15 tests in `CryptoBindingTests`:
- `Crypto_is_an_object`
- `GetRandomValues_returns_same_object`
- `GetRandomValues_fills_uint8_with_bytes_0_to_255`
- `GetRandomValues_statistically_not_all_zero`
- `GetRandomValues_works_with_Int32Array`
- `GetRandomValues_works_with_Uint32Array`
- `GetRandomValues_throws_for_Float32Array`
- `GetRandomValues_throws_for_Float64Array`
- `GetRandomValues_throws_TypeError_for_plain_array`
- `GetRandomValues_throws_for_oversized_buffer`
- `GetRandomValues_accepts_exactly_65536_bytes`
- `RandomUUID_matches_v4_format`
- `RandomUUID_returns_string`
- `RandomUUID_two_calls_differ`
- `Crypto_subtle_is_undefined`

## Handoff log

- 2026-05-21 — Implemented and verified. Bindings.Tests: 204 passed (189
  baseline + 15 new). Js.Tests: 1433 passed / 1 skipped. netclaw.dev
  `crypto.getRandomValues() not supported` error eliminated. Remaining
  engine.js error: GTM `not a function: undefined (callee hint: 'Nf')` at
  gtag/js line 293 — unrelated to crypto.
