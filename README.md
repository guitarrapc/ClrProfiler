# ClrProfiler

**ClrProfiler** is a zero-dependency .NET library designed to monitor and collect detailed metrics on Contention Events, Garbage Collection (GC), Processes, Threads, and ThreadPool activities through EventListener. This tool is essential for developers aiming to gain in-depth insights into the performance and behavior of their .NET applications.

## Key Features

- **Comprehensive Monitoring**
  ClrProfiler captures a wide range of CLR events, providing a holistic view of your application's runtime performance.
- **Cloud Tracing Integration**
  Seamlessly integrates with cloud tracing services, with built-in support for Datadog, enabling real-time monitoring and analytics.
- **Ease of Use**
  Designed for simplicity, ClrProfiler allows for straightforward integration into your projects, facilitating immediate performance tracking without the need for complex configurations.

## Benchmarks

`src/ClrProfiler.Benchmarks` measures representative GC, contention, and ThreadPool workloads with ClrProfiler disabled and enabled. BenchmarkDotNet reports execution time, allocated bytes, and GC collection counts for both conditions.


## Getting Started

To utilize ClrProfiler with Datadog metrics, include the `ClrProfiler.DatadogTracing` package in your project. Initialize the Dogstatsd and enable the CLR tracker as demonstrated below:

```sh
dotnet add package ClrProfiler.DatadogTracing
```

Start Dogstatsd and ClrTracker.

```cs
// Run Dogstatsd with UDP
var dogstatsdConfig = new StatsdConfig
{
    StatsdServerName = host,
    StatsdPort = port,
    ConstantTags = ["app:YourAppName"],
};
DogStatsd.Configure(dogstatsdConfig);

// enable clr tracker
using var tracker = new ClrTracker(loggerFactory);
tracker.EnableTracker(); // required, enable clr tracker explicitly
tracker.StartTracker();
```

Now you are ready to use ClrTracker on your application. Metrics will be sent to Datadog by dogstatsd.

### Select instrumentation

All instrumentation remains enabled by default. For lower overhead, select only the CLR events and timer samples your application consumes:

```cs
using var tracker = new ClrTracker(loggerFactory, new ClrTrackerOptions
{
    TrackerType = ClrTrackerType.Datadog,
    EnabledFeatures = ProfilerFeature.GCEvent
        | ProfilerFeature.ThreadPoolEvent
        | ProfilerFeature.ContentionEvent,
});
tracker.EnableTracker();
tracker.StartTracker();
```

Unselected features do not create a listener, subscribe to runtime events, start a reader, or create a timer. The same `EnabledFeatures` option is available on `ProfilerTrackerOptions` when using the core package directly.

### Add custom instrumentation

Implement `IProfiler` to monitor another `EventSource`, CLR event, or timer source, then register a factory alongside the built-in instrumentation:

```cs
using var tracker = new ClrTracker(loggerFactory, new ClrTrackerOptions
{
    TrackerType = ClrTrackerType.Datadog,
    EnabledFeatures = ProfilerFeature.GCEvent,
    AdditionalProfilerFactories =
    [
        () => new MyEventSourceProfiler(...),
    ],
});
tracker.EnableTracker();
tracker.StartTracker();
```

Each factory is invoked once when `ProfilerTracker` is constructed. When using `ClrTracker`, this happens inside `EnableTracker()` for Datadog, Logger, and Custom tracker types. The tracker owns the returned profiler and includes it in `Start`, `Stop`, `Restart`, `Cancel`, and `Dispose`. Custom profilers remain responsible for bounded, non-blocking event processing and callback error handling.

`IProfiler.DroppedEventCount` reports how many events a profiler discarded; it defaults to `0`, so a profiler written before the member existed keeps compiling. A custom profiler that drops events should report its own cumulative count so the profiler diagnostics metric below covers it.

### Observe what the profiler itself dropped

Every bounded queue in ClrProfiler retains the newest values and discards the rest, so a reader that cannot keep up loses data. `ProfilerFeature.ProfilerDiagnosticsTimer` (enabled by default) samples `IProfiler.DroppedEventCount` for every profiler the tracker owns and emits one `ProfilerDiagnosticsStatistics` per profiler per tick, on the same `TimerOption` interval as the other timers.

```
clr_diagnostics_timer.profiler.dropped_event_count:0|g|#app:YourAppName,profiler:GCEventProfiler
clr_diagnostics_timer.profiler.dropped_event_count:0|g|#app:YourAppName,profiler:ContentionEventProfiler
```

The value is cumulative for the profiler's lifetime, so read a `diff` or `derivative` of the series. Any non-zero rate means the `clr_diagnostics_event.*` metrics are undercounting for that profiler over the same window; treat a persistently rising count as a signal that the reader is starved rather than as a signal about the application.

Because the counts are cumulative, a stalled reader delays this metric instead of corrupting it: the newest sample still carries the true total. The diagnostics reader is independent of the other listeners' readers, so a listener whose reader stalls still has its rising count reported.

The `profiler` tag is bounded to the built-in profiler names; any other name, including one from `AdditionalProfilerFactories`, is reported as `profiler:unknown` so a caller-controlled string cannot grow the metric's cardinality.

### GC heap statistics and loss-free pause time

`ProfilerFeature.GCEvent` also parses the runtime's `GCHeapStats_V1`/`GCHeapStats_V2` event, which the CLR emits at the end of every collection under the same GC keyword. Each heap-stats event carries the exact post-collection heap state and is delivered as `GCEventStatistics` with `GCEventType.GCHeapStats`:

```
clr_diagnostics_event.gc.heapstats_size_bytes:1234|g|#app:YourAppName,gc_gen:0
clr_diagnostics_event.gc.heapstats_size_bytes:1234|g|#app:YourAppName,gc_gen:poh
clr_diagnostics_event.gc.heapstats_finalization_promoted_bytes:0|g|#app:YourAppName
clr_diagnostics_event.gc.heapstats_pinned_object_count:3|g|#app:YourAppName
clr_diagnostics_event.gc.heapstats_gc_handle_count:521|g|#app:YourAppName
```

Sizes are tagged `gc_gen:0|1|2|loh|poh`. Runtimes that emit the V1 payload predate the pinned object heap, so `gc_gen:poh` reports zero there.

`ProfilerFeature.GCInfoTimer` samples complement the event-based metrics with runtime counters that cannot drop:

- `clr_diagnostics_timer.gc.total_pause_time_ms` carries `GC.GetTotalPauseDuration()`, the cumulative milliseconds the runtime paused for GC since process start. Read a `diff` or `derivative` of the series for a loss-free pause-time rate; it stays exact even when `clr_diagnostics_event.gc.*` undercounts under load.
- Generation sizes (`clr_diagnostics_timer.gc.gc_size`) come from the public `GC.GetGCMemoryInfo().GenerationInfo` API rather than private reflection, so they keep working across runtime updates.

CLR event subscription starts in `StartTracker()`, after the callback handlers are registered — no event can arrive between `EnableTracker()` and `StartTracker()` only to be discarded unseen. If an event ever reaches a listener without a registered handler, it is counted into `DroppedEventCount` instead of being silently lost. The GC event delivery channel retains 512 values (other listeners retain 50) because one collection emits start/end, suspend, and heap-stats values and gen0 bursts arrive faster than a metric backend flushes.

## Debugging

If you want debug behaviour, use ClrTrackerType.Logger instead. This will log metrics to ILogger.Debug.

```cs
// enable clr tracker
using var tracker = new ClrTracker(loggerFactory, new ClrTrackerOptions
{
    TrackerType = ClrTrackerType.Logger
});
tracker.EnableTracker();
tracker.StartTracker();
```

Metric tags are precomputed when the tracker is enabled, before CLR listeners start. Runtime values that are newer than the known tag mappings use a bounded `unknown` tag instead of creating an unbounded cache entry or throwing. Logger metric projection avoids formatting work when debug logging is disabled.

## Custom Profiling

This section customizes handling for the built-in instrumentation; use `AdditionalProfilerFactories` above to add new instrumentation. Implement the `IClrTrackerCallbackHandler` interface to define custom behavior for each built-in CLR event type.

```cs
public class MyCustomTrackerHandler : IClrTrackerCallbackHandler
{
    private readonly IMyMetricsService _metrics;

    public MyCustomTrackerHandler(IMyMetricsService metrics)
    {
        _metrics = metrics;
    }

    public Task OnGCEventAsync(GCEventStatistics statistics)
    {
        // Custom GC event handling
        _metrics.RecordGC(statistics);
        return Task.CompletedTask;
    }

    public Task OnContentionEventAsync(ContentionEventStatistics statistics)
    {
        // Custom contention event handling
        _metrics.RecordContention(statistics);
        return Task.CompletedTask;
    }

    public Task OnThreadPoolEventAsync(ThreadPoolEventStatistics statistics)
    {
        // Custom threadpool event handling
        _metrics.RecordThreadPool(statistics);
        return Task.CompletedTask;
    }

    public Task OnGCInfoTimerAsync(GCInfoStatistics statistics)
    {
        // Custom GC info timer handling
        _metrics.RecordGCInfo(statistics);
        return Task.CompletedTask;
    }

    public Task OnProcessInfoTimerAsync(ProcessInfoStatistics statistics)
    {
        // Custom process info timer handling
        _metrics.RecordProcessInfo(statistics);
        return Task.CompletedTask;
    }

    public Task OnThreadInfoTimerAsync(ThreadInfoStatistics statistics)
    {
        // Custom thread info timer handling
        _metrics.RecordThreadInfo(statistics);
        return Task.CompletedTask;
    }

    // Optional. Defaults to ignoring the sample, so an existing handler keeps compiling.
    // Override it to see how much data each profiler discarded.
    public Task OnProfilerDiagnosticsTimerAsync(ProfilerDiagnosticsStatistics statistics)
    {
        _metrics.RecordProfilerDiagnostics(statistics);
        return Task.CompletedTask;
    }

    public void OnException(Exception exception)
    {
        // Custom exception handling
        _metrics.RecordError(exception);
    }
}
```

Use your custom handler by specifying `ClrTrackerType.Custom` and providing your handler implementation:

```cs
using var tracker = new ClrTracker(loggerFactory, new ClrTrackerOptions
{
    TrackerType = ClrTrackerType.Custom,
    CustomHandler = new MyCustomTrackerHandler(metricsService)
});
tracker.EnableTracker();
tracker.StartTracker();
```

This approach allows you to integrate ClrProfiler with any metrics backend or implement custom logic for processing CLR events.

Bounded delivery queues keep producers non-blocking and retain the newest values when a reader falls behind. `GCEventListener`, `ThreadPoolEventListener`, `GCInfoTimerListener`, `ProcessInfoTimerListener`, `ThreadInfoTimerListener`, and `ProfilerDiagnosticsTimerListener` expose a cumulative `DroppedEventCount` for values evicted from their delivery channels. The counter is thread-safe and is not reset by stop or restart. Each count is also surfaced as a metric; see [Observe what the profiler itself dropped](#observe-what-the-profiler-itself-dropped).

Contention callbacks place samples in a fixed-capacity MPSC queue and aggregate them by contention flag on the single reader. One `ContentionEventStatistics` can therefore represent many accepted events, while its `Count`, duration sum, maximum, and timestamp always come from the same delivery window.

| Member | Meaning |
| --- | --- |
| `Count` | Number of contention events represented by this value. |
| `DurationNsSum` | Total contention duration in nanoseconds across those events. |
| `DurationNsMax` | Longest single contention duration in nanoseconds among those events. |
| `DurationNsMean` | `DurationNsSum / Count`, or 0 when nothing was aggregated. |
| `Time` | Timestamp of the newest event observed for the flag. The duration fields summarize the whole window, so they are not tied to this timestamp. |

The producer never waits for the reader and does not use an unbounded spin loop. Queue reservation has a fixed retry limit; a sample is rejected when the queue is full or that limit is exhausted. `ContentionEventListener.DroppedEventCount` exposes the cumulative rejected count, which is also exported as `clr_diagnostics_timer.profiler.dropped_event_count{profiler:ContentionEventProfiler}`. Durations of accepted samples accumulate as whole picoseconds; non-finite or negative durations contribute 0 while still being counted.

Aggregates are reset per dispatch. For accepted samples, every individual value and the totals across dispatches are exact.

Stopping the profiler advances the queue generation. Samples accepted before a stop are dispatched separately from samples accepted after a restart, even when the reader drains both generations later.

A dispatch happens every time the reader drains, which is far more often than a metrics backend flushes. Contention metrics therefore only use statsd types that aggregate correctly over many submissions within one flush interval:

| Metric | statsd type | Value | Why the type |
| --- | --- | --- | --- |
| `clr_diagnostics_event.contention.startend_count` | counter | `Count` | Counts add across every accepted dispatch. |
| `clr_diagnostics_event.contention.startend_duration_ns_sum` | counter | `DurationNsSum` | Sums add across accepted samples. |
| `clr_diagnostics_event.contention.startend_duration_ns_max` | histogram | `DurationNsMax` | The maximum of the accepted window maxima is the accepted maximum for the interval. |

Read `startend_duration_ns_max.max` for the worst accepted contention in an interval, and `startend_duration_ns_sum / startend_count` for the exact mean of accepted samples. The `.avg`, `.count`, and percentile series derived from the histogram describe window maxima rather than individual contentions, so do not read them as durations.

A gauge is deliberately not used for any of these. A gauge keeps only the last value submitted within a flush interval, which would discard every aggregation window but one.

## Sandbox

Run ConsoleApp, then metrics ingested will shown on Console. Sandbox runs both Server and Client. Server is listen UDP Server on `127.0.0.1:8125` and accept request from local datadog agent.
You will see following messages.

```
clr_diagnostics_event.gc.startend_count:17|c|#app:ConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced
clr_diagnostics_event.gc.suspend_object_count:120|c|#app:ConsoleApp,gc_suspend_reason:gc

clr_diagnostics_event.gc.startend_duration_ms:2.4244|g|#app:ConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced
clr_diagnostics_event.gc.suspend_duration_ms:2.8953|g|#app:ConsoleApp,gc_suspend_reason:gc

clr_diagnostics_event.gc.suspend_object_count:475|c|#app:ConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_count:19|c|#app:ConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_duration_ms:0.9144|g|#app:ConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_duration_ms:1.0946|g|#app:ConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_object_count:783|c|#app:ConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_count:18|c|#app:ConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_duration_ms:2.7549|g|#app:ConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_duration_ms:2.7791|g|#app:ConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced
clr_diagnostics_event.threadpool.adjustment_avg_throughput:0.00017109293075546954|g|#app:ConsoleApp,thread_adjust_reason:warmup
clr_diagnostics_event.threadpool.adjustment_new_workerthreads_count:17|g|#app:ConsoleApp,thread_adjust_reason:warmup

clr_diagnostics_event.gc.suspend_object_count:1178|c|#app:ConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_count:19|c|#app:ConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_duration_ms:0.7547999999999999|g|#app:ConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_duration_ms:2.6473|g|#app:ConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

datadog.dogstatsd.client.metrics:362|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp
datadog.dogstatsd.client.events:0|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp
datadog.dogstatsd.client.service_checks:0|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp
datadog.dogstatsd.client.bytes_sent:1928|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp
datadog.dogstatsd.client.bytes_dropped:0|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp
datadog.dogstatsd.client.packets_sent:8|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp
datadog.dogstatsd.client.packets_dropped:0|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp
datadog.dogstatsd.client.packets_dropped_queue:0|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp
datadog.dogstatsd.client.aggregated_context_by_type:10|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp,metrics_type:gauge
datadog.dogstatsd.client.aggregated_context_by_type:8|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp,metrics_type:count
datadog.dogstatsd.client.aggregated_context_by_type:0|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp,metrics_type:set
clr_diagnostics_event.gc.suspend_object_count:1539|c|#app:ConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_count:19|c|#app:ConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_duration_ms:2.0896|g|#app:ConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_duration_ms:2.8951|g|#app:ConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced
```
