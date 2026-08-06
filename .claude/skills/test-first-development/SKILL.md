---
name: test-first-development
description: Mandatory test-first workflow for changes under `src/` in ClrProfiler. Covers CLR EventListener parsing and correlation, bounded-channel delivery, timer sampling, profiler and tracker lifecycle, callback error handling, statistics values, Datadog or logger metric projection, multi-target builds, and regression verification with xUnit v3.
---

# Test-First Development

Use this skill for every task that adds or modifies code under `src/`. Skip it only for changes limited to documentation, configuration, generated artifacts, or the `.agents` directory.

Also apply `performance-requirements` when changing per-event, per-sample, or per-metric paths.

## Red-green workflow

1. Add the smallest test that demonstrates the missing or incorrect observable behavior.
2. Run that test against the current production code and confirm it fails for the expected reason. A compile failure is acceptable for a new API.
3. Implement the minimum production change that makes the test pass.
4. Re-run the targeted test, then its containing test project.
5. Run the full Release build and test suite used by CI.
6. For an observable API or usage change, update `README.md` in the same change.

For a behavior-preserving performance refactor, first prove the relevant tests pass and capture a repeatable allocation or throughput baseline. Treat that baseline as red, then require behavioral parity after the change.

## Select the test project

- Core listeners, timers, statistics, `ProfilerTracker`, event integrity, and lifecycle: `tests/ClrProfiler.UnitTest/ClrProfiler.UnitTest.csproj`
- Datadog adapter, logger adapter, tag and metric mapping, and `ClrTracker`: `tests/CleProfiler.DatadogTracing.UnitTest/CleProfiler.DatadogTracing.UnitTest.csproj`

The `CleProfiler.DatadogTracing.UnitTest` directory contains an existing `CleProfiler` spelling. Use the path as it exists; do not rename it as an incidental cleanup.

This repository uses xUnit v3. Use `[Fact]` and `[Theory]`, synchronous `Assert` APIs, and `dotnet test` filtering when a targeted run is useful:

```powershell
dotnet test tests/ClrProfiler.UnitTest/ClrProfiler.UnitTest.csproj -c Release --filter "FullyQualifiedName~ProfilerLifecycleTest"
dotnet test tests/CleProfiler.DatadogTracing.UnitTest/CleProfiler.DatadogTracing.UnitTest.csproj -c Release --filter "FullyQualifiedName~DatadogTracingUnitTest"
```

Finish with the CI-equivalent sequence:

```powershell
dotnet build -c Release
dotnet test -c Release --no-build --logger "console;verbosity=normal"
dotnet pack -c Release --no-build
```

## Test the right boundary

- Prefer deterministic tests through public APIs or existing `internal` seams exposed with `InternalsVisibleTo`.
- For raw CLR payload parsing, feed synthetic event names, timestamps, and payloads into the narrow listener seam. Do not require real GC or scheduler timing when a deterministic payload answers the question.
- Use real runtime events only for integration behavior that cannot be represented synthetically. Put such tests in the non-parallel collection when they alter process-wide GC, EventSource, DogStatsD, port, or tracker state.
- Use loopback or fakes for metric transport. Bound all waits with `CancellationTokenSource`, `WaitAsync`, or an equivalent timeout.
- Do not use arbitrary `Task.Delay` as the primary assertion mechanism. Signal completion with `TaskCompletionSource` created with `RunContinuationsAsynchronously`.
- Assert callback count, order, payload fields, and error forwarding explicitly. Avoid assertions that only prove that no exception was thrown.

## Required regression coverage

Choose the applicable cases; do not add unrelated tests.

### Event listeners and channels

- Recognized and ignored event names, including versioned names.
- Missing, null, differently typed, malformed, and out-of-range payload fields.
- Correct statistics fields and tick-to-duration conversion.
- Event order and behavior at channel capacity; make intentional loss policy explicit.
- Callback exception reporting followed by successful processing of a later event.
- Reader cancellation and continued reading across `Stop`/`Restart`.

### GC correlation

- A normal start/end pair.
- Overlapping foreground and background collections completed in a different order.
- An end observed without its start.
- Stale or colliding bounded correlation state when the implementation makes that possible.
- Suspend/restart correlation independently from collection start/end correlation.

### Timers and lifecycle

- Independent state for multiple listeners or trackers.
- Idempotent repeated lifecycle calls.
- Stop/restart behavior without spawning duplicate readers or timers.
- Cancellation, reset, and disposal ownership.
- A disposed timer or tracker cannot recreate owned resources.

### Metric adapters

- Exact metric name, type, value, and tags for every affected statistics variant.
- Enum/reason mapping, unknown values, and unit conversion.
- Logger and Datadog projections remain aligned where they intentionally expose the same metrics.
- Cache behavior does not leak mutable arrays or grow from arbitrary input.

## Multi-target and compatibility checks

The core library targets `net8.0`, `net9.0`, and `net10.0`; the Datadog adapter targets `net8.0` and `net9.0`; tests currently run on `net9.0`. A passing test target does not prove all library targets compile, so always run the solution build after changing production code.

Keep `ClrProfiler` zero-dependency. Do not move adapter packages into the core project to simplify a test.
