# ClrProfiler

**ClrProfiler** is a zero-dependency .NET library designed to monitor and collect detailed metrics on Contention Events, Garbage Collection (GC), Processes, Threads, and ThreadPool activities through EventListener. This tool is essential for developers aiming to gain in-depth insights into the performance and behavior of their .NET applications.

## Key Features

- **Comprehensive Monitoring**
  ClrProfiler captures a wide range of CLR events, providing a holistic view of your application's runtime performance.
- **Cloud Tracing Integration**
  Seamlessly integrates with cloud tracing services, with built-in support for Datadog, enabling real-time monitoring and analytics.
- **Ease of Use**
  Designed for simplicity, ClrProfiler allows for straightforward integration into your projects, facilitating immediate performance tracking without the need for complex configurations.
- **No Silent Data Loss**
  Event delivery is non-blocking and bounded, and anything the profiler had to discard is counted and exported as a metric, so you always know when data is incomplete.

## Packages

| Package | Description | Target Frameworks |
| --- | --- | --- |
| `ClrProfiler` | Core library. Zero dependencies. Use this to receive CLR statistics with your own callbacks. | `net8.0`, `net9.0`, `net10.0` |
| `ClrProfiler.DatadogTracing` | Datadog (DogStatsD) and `ILogger` adapters on top of the core library. | `net8.0`, `net9.0` |

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

CLR event subscription starts in `StartTracker()`, after the callback handlers are registered, so no event is delivered before there is a handler to observe it. Events that occur before `StartTracker()` (for example GCs during application startup) are not captured as events; the cumulative timer metrics below still cover their totals. Use `StopTracker()` / `RestartTracker()` to pause and resume collection (events during a stop are not collected), and `CancelTracker()` or `Dispose()` to end it.

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

Available features:

| `ProfilerFeature` | Source | What you get |
| --- | --- | --- |
| `GCEvent` | CLR events | GC start/end duration, suspension (pause) duration, post-collection heap statistics |
| `ThreadPoolEvent` | CLR events | ThreadPool worker adjustments, starvation detection |
| `ContentionEvent` | CLR events | Monitor lock contention count and durations |
| `GCInfoTimer` | Periodic sampling | Heap size, allocation bytes, GC counts, generation sizes, cumulative pause time |
| `ThreadInfoTimer` | Periodic sampling | ThreadPool thread counts, queue length, lock contention count |
| `ProcessInfoTimer` | Periodic sampling | CPU, private bytes, working set |
| `ProfilerDiagnosticsTimer` | Periodic sampling | How many events each profiler dropped (see below) |

Unselected features do not create a listener, subscribe to runtime events, start a reader, or create a timer. The same `EnabledFeatures` option is available on `ProfilerTrackerOptions` when using the core package directly.

Timer-based features sample once per minute by default. The interval is configurable through `ProfilerTrackerOptions.TimerOption` when using the core `ClrProfiler` package directly; `ClrTracker` uses the default interval.

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

## Metrics

Metric names are stable and grouped by origin: `clr_diagnostics_event.*` comes from CLR events as they happen, and `clr_diagnostics_timer.*` comes from periodic sampling (every minute by default).

### Event metrics

| Metric | statsd type | Tags | Description |
| --- | --- | --- | --- |
| `clr_diagnostics_event.gc.startend_count` | count | `gc_gen`, `gc_type`, `gc_reason` | Completed garbage collections. |
| `clr_diagnostics_event.gc.startend_duration_ms` | gauge | `gc_gen`, `gc_type`, `gc_reason` | Duration of a collection in milliseconds. For background GC this is wall-clock time including the concurrent phase, not pause time. |
| `clr_diagnostics_event.gc.suspend_object_count` | count | `gc_suspend_reason` | Suspension count reported by the runtime's suspend event. |
| `clr_diagnostics_event.gc.suspend_duration_ms` | gauge | `gc_suspend_reason` | How long the execution engine was suspended (application pause) in milliseconds. |
| `clr_diagnostics_event.gc.heapstats_size_bytes` | gauge | `gc_gen:0\|1\|2\|loh\|poh` | Per-generation heap size after each collection, from `GCHeapStats_V1`/`GCHeapStats_V2`. Runtimes emitting the V1 payload predate the pinned object heap, so `gc_gen:poh` reports zero there. |
| `clr_diagnostics_event.gc.heapstats_finalization_promoted_bytes` | gauge | — | Bytes promoted because of finalization in each collection. |
| `clr_diagnostics_event.gc.heapstats_pinned_object_count` | gauge | — | Pinned objects observed by each collection. |
| `clr_diagnostics_event.gc.heapstats_gc_handle_count` | gauge | — | GC handles in use at the end of each collection. |
| `clr_diagnostics_event.contention.startend_count` | count | `contention_type` | Monitor lock contentions. |
| `clr_diagnostics_event.contention.startend_duration_ns_sum` | count | `contention_type` | Total contention duration in nanoseconds. Divide by `startend_count` for the exact mean. |
| `clr_diagnostics_event.contention.startend_duration_ns_max` | histogram | `contention_type` | Longest single contention per aggregation window. Read the `.max` series for the worst contention in a flush interval; the `.avg`, `.count`, and percentile series describe window maxima, not individual contentions. |
| `clr_diagnostics_event.threadpool.available_workerthread_count` | gauge | — | Active worker threads when a worker stops. |
| `clr_diagnostics_event.threadpool.adjustment_avg_throughput` | gauge | `thread_adjust_reason` | Average throughput measured by the ThreadPool hill-climbing algorithm. |
| `clr_diagnostics_event.threadpool.adjustment_new_workerthreads_count` | gauge | `thread_adjust_reason` | New worker thread count after an adjustment. |

When a ThreadPool adjustment is caused by starvation, the Datadog tracker additionally submits a Datadog Event (`ThreadPool Starvation detected`, alert type `warning`) so you can alert on it directly.

Contention metrics use only statsd types that aggregate correctly across many submissions within one flush interval (counts add up, histogram `.max` stays the true maximum). A gauge is deliberately not used for them: a gauge keeps only the last value per flush interval, which would discard every aggregation window but one.

Tag values are bounded. Values the library does not recognize (for example from a newer runtime) are reported as `unknown` instead of creating unbounded tag cardinality:

- `gc_gen`: `0|1|2` (`0|1|2|loh|poh` on heap-stats sizes)
- `gc_type`: `0|1|2`
- `gc_reason`: `soh|induced|low_memory|empty|loh|oos_soh|oos_loh|incuded_non_forceblock|stress_testing|finalizer_low_memory_induced|user_gc_request`
- `gc_suspend_reason`: `other|gc|appdomain_shudown|code_pitch|shutdown|debugger|prep_gc`
- `thread_adjust_reason`: `warmup|initializing|random_move|climbing_move|change_point|stabilizing|starvation|timedout|cooperative_blocking`
- `contention_type`: `0|1`

### Timer metrics

Sampled every minute by default. GC timer metrics carry `gc_mode:Workstation|Server`, `latency_mode`, and `compaction_mode` tags; process and thread metrics have no metric-specific tags.

| Metric | statsd type | Tags | Description |
| --- | --- | --- | --- |
| `clr_diagnostics_timer.gc.heap_size_bytes` | gauge | GC mode tags | Managed heap size (`GC.GetTotalMemory`). |
| `clr_diagnostics_timer.gc.total_allocation_bytes` | gauge | GC mode tags | Cumulative allocated bytes since process start. Read a `diff` for the allocation rate. |
| `clr_diagnostics_timer.gc.gc_count` | gauge | `gc_gen:0\|1\|2` + GC mode tags | Cumulative collection count per generation. |
| `clr_diagnostics_timer.gc.gc_size` | gauge | `gc_gen:0\|1\|2\|loh` + GC mode tags | Generation size after the most recent collection. |
| `clr_diagnostics_timer.gc.time_in_gc_percent` | gauge | GC mode tags | Percentage of time spent in the most recent GC. |
| `clr_diagnostics_timer.gc.total_pause_time_ms` | gauge | GC mode tags | Cumulative milliseconds the runtime paused for GC since process start (`GC.GetTotalPauseDuration()`). See below. |
| `clr_diagnostics_timer.process.cpu` | gauge | — | Process CPU usage. |
| `clr_diagnostics_timer.process.private_bytes` | gauge | — | Private memory bytes. |
| `clr_diagnostics_timer.process.working_sets` | gauge | — | Working set bytes. |
| `clr_diagnostics_timer.thread.available_worker_threads` / `available_completion_port_threads` | gauge | — | Available ThreadPool threads. |
| `clr_diagnostics_timer.thread.max_worker_threads` / `max_completion_port_threads` | gauge | — | ThreadPool maximums. |
| `clr_diagnostics_timer.thread.using_worker_threads` / `using_completion_port_threads` | gauge | — | Threads currently in use. |
| `clr_diagnostics_timer.thread.thread_count` | gauge | — | ThreadPool thread count. |
| `clr_diagnostics_timer.thread.queue_length` | gauge | — | Pending ThreadPool work item count. |
| `clr_diagnostics_timer.thread.lock_contention_count` | gauge | — | Cumulative Monitor lock contention count from the runtime. |
| `clr_diagnostics_timer.thread.completed_items_count` | gauge | — | Cumulative completed ThreadPool work items. |
| `clr_diagnostics_timer.profiler.dropped_event_count` | gauge | `profiler` | Cumulative events each profiler discarded. See below. |

### Loss-free counters vs. event metrics

Event metrics can undercount under extreme load (see the next section), so the timer metrics deliberately include cumulative runtime counters that cannot drop:

- `clr_diagnostics_timer.gc.total_pause_time_ms` stays exact even when `clr_diagnostics_event.gc.*` undercounts. Read a `diff` or `derivative` of the series for a loss-free pause-time rate.
- `clr_diagnostics_timer.gc.gc_count` and `clr_diagnostics_timer.gc.total_allocation_bytes` are cumulative for the process lifetime, independent of event delivery.
- Generation sizes (`clr_diagnostics_timer.gc.gc_size`) come from the public `GC.GetGCMemoryInfo().GenerationInfo` API rather than private reflection, so they keep working across runtime updates.

## Observe what the profiler itself dropped

Event delivery inside ClrProfiler never blocks your application: every listener buffers events in a bounded queue that keeps the newest values and discards the rest when a consumer cannot keep up. Nothing is lost silently — every discarded event is counted.

`ProfilerFeature.ProfilerDiagnosticsTimer` (enabled by default) samples `IProfiler.DroppedEventCount` for every profiler the tracker owns and emits one sample per profiler per tick, on the same interval as the other timers:

```
clr_diagnostics_timer.profiler.dropped_event_count:0|g|#app:YourAppName,profiler:GCEventProfiler
clr_diagnostics_timer.profiler.dropped_event_count:0|g|#app:YourAppName,profiler:ContentionEventProfiler
```

The value is cumulative for the profiler's lifetime, so read a `diff` or `derivative` of the series. Any non-zero rate means the `clr_diagnostics_event.*` metrics are undercounting for that profiler over the same window; treat a persistently rising count as a signal that the metric consumer is starved rather than as a signal about the application.

Because the counts are cumulative, a stalled consumer delays this metric instead of corrupting it: the newest sample still carries the true total. The diagnostics reader is independent of the other listeners' readers, so a listener whose reader stalls still has its rising count reported.

The `profiler` tag is bounded to the built-in profiler names; any other name, including one from `AdditionalProfilerFactories`, is reported as `profiler:unknown` so a caller-controlled string cannot grow the metric's cardinality.

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

Logger metric projection avoids formatting work when debug logging is disabled, so leaving the Logger tracker in place with debug logging off costs almost nothing.

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

Your callbacks run on dedicated reader tasks, never on the threads that emit CLR events, so a slow callback slows delivery to itself but never blocks the application. If a callback throws, the exception is routed to `OnException` and delivery continues.

Contention events are aggregated before delivery: one `ContentionEventStatistics` can represent many contention events observed in the same delivery window, summarized by contention flag.

| Member | Meaning |
| --- | --- |
| `Count` | Number of contention events represented by this value. |
| `DurationNsSum` | Total contention duration in nanoseconds across those events. |
| `DurationNsMax` | Longest single contention duration in nanoseconds among those events. |
| `DurationNsMean` | `DurationNsSum / Count`, or 0 when nothing was aggregated. |
| `Time` | Timestamp of the newest event observed for the flag. The duration fields summarize the whole window, so they are not tied to this timestamp. |

For accepted samples, the values and the totals across deliveries are exact. Samples that could not be accepted (queue full under extreme load) are counted into `DroppedEventCount` and surfaced by the diagnostics metric; see [Observe what the profiler itself dropped](#observe-what-the-profiler-itself-dropped).

## Sandbox

The `sandbox` folder contains two runnable samples:

- `sandbox/ConsoleApp` runs the Datadog tracker end to end in a single process: it starts a UDP server on `127.0.0.1:8125` that plays the role of a local Datadog agent, allocates memory and forces GCs, and prints every ingested metric to the console.
- `sandbox/CustomConsoleApp` demonstrates `ClrTrackerType.Custom` with an `IClrTrackerCallbackHandler` implementation that logs statistics through `ILogger`.

Running ConsoleApp shows messages like the following:

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
datadog.dogstatsd.client.bytes_sent:1928|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp
datadog.dogstatsd.client.packets_sent:8|c|#app:ConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:ConsoleApp
```

## Benchmarks

`src/ClrProfiler.Benchmarks` measures representative GC, contention, and ThreadPool workloads with ClrProfiler disabled and enabled. BenchmarkDotNet reports execution time, allocated bytes, and GC collection counts for both conditions.
