---
name: test-first-development
description: Mandatory test-first workflow for changes under `src/` in ClrProfiler. Covers red-green testing for CLR EventListener parsing and correlation, bounded-channel delivery, timer sampling, profiler and tracker lifecycle, callback error handling, statistics values, Datadog or logger metric projection, multi-target builds, and regression verification with TUnit.
---

# Test-First Development

Use this skill for every task that adds or modifies code under `src/`. Skip it only for changes limited to documentation, configuration, generated artifacts, or the `.agents` directory.

Also apply `performance-requirements` when changing per-event, per-sample, or per-metric paths.

## Workflow

### 1. Write a failing test first

Add the smallest test that proves the current behavior is wrong or missing.

- For a feature, exercise the new observable behavior and confirm a compile or assertion failure.
- For a bug, reproduce the defect and confirm that the test fails for the expected reason.
- For a behavior change, assert the new contract before changing production code.

Run the test with TUnit's tree-node filter:

```powershell
dotnet test --project tests/ClrProfiler.UnitTest/ClrProfiler.UnitTest.csproj -c Release --treenode-filter "/*/*/YourTestClass/YourTestMethod*"
```

For a behavior-preserving performance refactor, first prove the relevant tests pass and capture a repeatable allocation or throughput baseline. Treat the baseline as red and require behavioral parity after the change.

### 2. Implement the minimum change

Write only the production code needed to make the failing test pass, then rerun the same filtered test.

### 3. Run the containing project

Choose the project by ownership:

- Core listeners, timers, statistics, `ProfilerTracker`, event integrity, and lifecycle: `tests/ClrProfiler.UnitTest/ClrProfiler.UnitTest.csproj`
- Datadog or logger adapters, tags, metric mapping, and `ClrTracker`: `tests/CleProfiler.DatadogTracing.UnitTest/CleProfiler.DatadogTracing.UnitTest.csproj`

The `CleProfiler.DatadogTracing.UnitTest` directory contains an existing `CleProfiler` spelling. Use the path as it exists; do not rename it as incidental cleanup.

```powershell
dotnet test --project tests/ClrProfiler.UnitTest/ClrProfiler.UnitTest.csproj -c Release
dotnet test --project tests/CleProfiler.DatadogTracing.UnitTest/CleProfiler.DatadogTracing.UnitTest.csproj -c Release
```

### 4. Run repository verification

Run the Release build, all tests, and packaging before finishing:

```powershell
dotnet build -c Release
dotnet test -c Release --no-build
dotnet pack -c Release --no-build
```

If an unrelated pre-existing project prevents a solution-wide command, still verify every changed project and report the unrelated blocker precisely.

Update `README.md` when public API behavior or usage changes.

## TUnit conventions

The repository selects Microsoft Testing Platform in `global.json` and uses TUnit.

- Use `[Test]` for tests.
- Use `[Arguments(...)]` for inline test cases.
- Use `[MethodDataSource]` or `[ClassDataSource]` for generated or shared data.
- Use `--treenode-filter`. Do not use `dotnet test --filter`.
- Return `async Task` whenever the test contains a TUnit assertion.
- Await every fluent assertion; an unawaited assertion may not execute.
- Prefer one behavior per test and descriptive names such as `EventReader_CallbackThrows_ReportsErrorAndContinues`.

Run a class or method as follows:

```powershell
dotnet test --project tests/ClrProfiler.UnitTest/ClrProfiler.UnitTest.csproj --treenode-filter "/*/*/ProfilerLifecycleTest/*"
dotnet test --project tests/ClrProfiler.UnitTest/ClrProfiler.UnitTest.csproj --treenode-filter "/*/*/ProfilerLifecycleTest/EventReaderContinuesAfterStopAndRestart*"
```

Use TUnit assertions:

```csharp
await Assert.That(actual.Type).IsEqualTo(GCEventType.GCStartEnd);
await Assert.That(actualEvents).Count().IsEqualTo(1);
await Assert.That(actualEvents).Contains(value => value.Type == GCEventType.GCSuspend);
await Assert.That(condition).IsTrue();
await Assert.That(callback).Throws<ObjectDisposedException>();
```

Use `HasSingleItem()` when the returned element is needed:

```csharp
var item = await Assert.That(actualEvents).HasSingleItem();
await Assert.That(item.Type).IsEqualTo(GCEventType.GCStartEnd);
```

## Parallelism and cancellation

TUnit runs tests in parallel by default.

- Add `[NotInParallel]` to tests or classes that mutate process-wide CLR, GC, EventSource, DogStatsD, fixed-port, or tracker state.
- Prefer a named `[NotInParallel("resource-key")]` constraint when only tests sharing that resource must serialize.
- Do not recreate xUnit collection-definition classes.
- Bound asynchronous waits. Prefer a test method `CancellationToken` parameter with `[Timeout]` when the framework should own the timeout.
- When using the current context directly, read `TestContext.Current!.Execution.CancellationToken`.
- Create `TaskCompletionSource` with `TaskCreationOptions.RunContinuationsAsynchronously`.
- Do not use arbitrary `Task.Delay` as the primary synchronization or assertion mechanism.

## Test the right boundary

- Prefer deterministic tests through public APIs or stable `internal` seams exposed with `InternalsVisibleTo`.
- Feed synthetic event names, timestamps, and payloads into the narrow listener seam for CLR payload parsing.
- Use real runtime events only when synthetic input cannot prove the behavior.
- Use loopback or fakes for metric transport. Do not require a cloud agent.
- Assert callback count, order, payload fields, cancellation, and error forwarding explicitly.
- Do not use reflection to test private implementation details.

## Regression coverage

Choose only the applicable cases.

### Event listeners and channels

- Recognized and ignored versioned event names.
- Missing, null, differently typed, malformed, and out-of-range payload fields.
- Correct statistics fields and tick-to-duration conversion.
- Event order and behavior at channel capacity, including the intentional loss policy.
- Callback failure reporting followed by successful processing of a later event.
- Reader cancellation and continued reading across `Stop` and `Restart`.

### GC correlation

- A normal start/end pair.
- Overlapping foreground and background collections completed in a different order.
- An end observed without its start.
- Stale or colliding bounded correlation state.
- Suspend/restart correlation independently from collection start/end correlation.

### Timers and lifecycle

- Independent state for multiple listeners and trackers.
- Idempotent repeated lifecycle calls.
- Stop/restart without duplicate readers or timers.
- Cancellation, reset, and disposal ownership.
- Prevention of resource recreation after disposal.

### Metric adapters

- Exact metric name, type, value, and tags for every affected statistics variant.
- Enum and reason mapping, unknown values, and unit conversion.
- Alignment between logger and Datadog projections where they expose the same metrics.
- Cache behavior that does not leak mutable arrays or grow from arbitrary input.

## Multi-target compatibility

The core library targets `net8.0`, `net9.0`, and `net10.0`; the Datadog adapter targets `net8.0` and `net9.0`; tests run on `net9.0`. Always build production projects after tests because a passing test target does not prove all target frameworks compile.

Keep `ClrProfiler` zero-dependency. Do not move adapter packages into the core project to simplify a test.
