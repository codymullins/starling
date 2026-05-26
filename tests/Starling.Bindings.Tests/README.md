# Starling.Bindings.Tests

Tests for the Web APIs that connect the Starling JS engine to the Starling
DOM (`src/Starling.Bindings`).

## What it covers

`querySelector`, `innerHTML`, `fetch`, `XMLHttpRequest`, timers, storage
(`localStorage` and `sessionStorage`), observers (`MutationObserver`,
`IntersectionObserver`), and HTML-spec script loading. The tests drive a
full `StarlingEngine` so they catch wiring bugs.

[`Starling.Bindings.Jint.Tests`](../Starling.Bindings.Jint.Tests) runs the
same surface against the Jint backend.

## How to run

```bash
dotnet test tests/Starling.Bindings.Tests
dotnet test tests/Starling.Bindings.Jint.Tests
```

## What the badge means

Coverage matches what real sites need. netclaw.dev runs end to end,
including its own code, Cloudflare scripts, and Google Analytics. Service
workers, web workers, streams, and WebRTC are not yet in — tracked in
[`10_WEB_APIS.md`](../../browser-plan/10_WEB_APIS.md).
