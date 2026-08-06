---
name: performance-requirements
description: ClrProfiler-specific performance and memory requirements for CLR EventListener callbacks, GC event correlation, bounded channels, timer sampling, statistics values, callback dispatch, and Datadog or logger metric projection. Use when changing event ingestion, listener or timer hot paths, statistics models, tracker concurrency, or metric formatting and tag caching.
---

# Performance Requirements

Treat overhead added to the profiled process as part of ClrProfiler's correctness. Apply these requirements to changes in `src/ClrProfiler` and `src/ClrProfiler.DatadogTracing` that run per CLR event, per timer tick, or per emitted metric.

## Protect the producer path

- Keep `EventListener.OnEventWritten`, `EventCreatedHandler`, and `ProcessEvent` bounded and non-blocking. Never wait synchronously for user callbacks, logging, network I/O, or channel capacity.
- Parse only the payload fields needed by the matched event. Prefer typed payload values and invariant conversion; avoid `ToString` plus `Parse` on the normal path when the runtime already supplies a numeric value.
- Avoid LINQ, closures, temporary collections, interpolated diagnostic strings, and per-event lookup construction in listener callbacks.
- Keep exception handling at the event boundary. Route malformed payload and callback failures to the configured error callback without terminating the reader loop.
- Do not silently change delivery semantics. Channel capacity, full mode, reader/writer assumptions, event ordering, and loss behavior are observable design decisions and require explicit tests.

## Preserve event correlation

- Correlate paired events using runtime identity, not arrival adjacency. In particular, background and foreground GCs can overlap, so match `GCStart` and `GCEnd` by GC index.
- Bound correlation state and avoid per-event allocation. If a fixed-size structure is used, test collisions, missing starts, stale entries, and overlapping collections before changing its capacity or indexing.
- Use `DateTime.Ticks` or the existing raw numeric representation through correlation, then calculate durations once when producing the statistics value.
- Treat unexpected event names, versions, missing payloads, and numeric representations as input-boundary cases. Do not let them corrupt state for subsequent events.

## Keep data and dispatch inexpensive

- Prefer compact `readonly struct` statistics for immutable per-sample values. Preserve value equality and hash-code behavior when adding fields.
- Pass large statistics by `in` where the existing metric projection API does so. Do not box statistics or enums on hot paths without measuring the cost.
- Cache reusable metric tags and mappings outside per-event methods. Keep caches bounded by a small runtime-defined key space; do not cache arbitrary user-controlled strings indefinitely.
- Keep the core `ClrProfiler` project dependency-free. Backend-specific formatting and delivery belong in `ClrProfiler.DatadogTracing` or another adapter.
- Await user callbacks in the single reader path so callback order remains deterministic. Continue reading after reporting a callback exception.

## Concurrency and lifecycle

- Keep listener, timer, and tracker instances independent. Do not introduce mutable static lifecycle state.
- Make `Start`, `Stop`, `Restart`, `Cancel`, `Reset`, and `Dispose` transitions safe and idempotent where the public API already promises that behavior.
- Keep a reader alive across `Stop` and `Restart`; cancellation owns reader termination. Disposal must release `EventListener`, `Timer`, channel-related, and cancellation resources without resurrecting them.
- Never hold a lifecycle or correlation lock while invoking user code, awaiting a task, emitting metrics, or performing I/O.
- Use `RunContinuationsAsynchronously` for completion sources in concurrent tests to avoid running test continuations inside production critical sections.

## Verify performance changes

The repository currently has no committed benchmark project, so do not claim a numeric performance improvement from stopwatch timing or from the functional test suite.

For a meaningful hot-path change:

1. Run the relevant correctness and data-integrity tests first.
2. Measure the old and new implementations with the same Release build, runtime, event payload, event count, warmup, and invocation method.
3. Record throughput and allocated bytes. Also inspect Gen0 collections when allocation behavior changes.
4. Add a focused BenchmarkDotNet project or benchmark only when repeatable performance gating is part of the task; keep it separate from the sample applications.
5. Reject unexplained event loss, ordering changes, unbounded state, or allocation regressions even when mean time improves.

Always run the repository's Release build and tests after a performance-sensitive change:

```powershell
dotnet build -c Release
dotnet test -c Release --no-build
```
