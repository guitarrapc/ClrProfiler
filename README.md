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

### Add custom profilers

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
```

Each factory is invoked once when the tracker is enabled. The tracker owns the returned profiler and includes it in `Start`, `Stop`, `Restart`, `Cancel`, and `Dispose`. Custom profilers remain responsible for bounded, non-blocking event processing and callback error handling.

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

For advanced scenarios, you can implement custom profiling by creating your own callback handler. Implement the `IClrTrackerCallbackHandler` interface to define custom behavior for each CLR event type.

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

Bounded delivery queues keep producers non-blocking and retain the newest values when a reader falls behind. `GCEventListener`, `ThreadPoolEventListener`, `GCInfoTimerListener`, `ProcessInfoTimerListener`, and `ThreadInfoTimerListener` expose a cumulative `DroppedEventCount` for values evicted from their delivery channels. The counter is thread-safe and is not reset by stop or restart.

Contention callbacks place samples in a fixed-capacity MPSC queue and aggregate them by contention flag on the single reader. One `ContentionEventStatistics` can therefore represent many accepted events, while its `Count`, duration sum, maximum, and timestamp always come from the same delivery window.

| Member | Meaning |
| --- | --- |
| `Count` | Number of contention events represented by this value. |
| `DurationNsSum` | Total contention duration in nanoseconds across those events. |
| `DurationNsMax` | Longest single contention duration in nanoseconds among those events. |
| `DurationNsMean` | `DurationNsSum / Count`, or 0 when nothing was aggregated. |
| `Time` | Timestamp of the newest event observed for the flag. The duration fields summarize the whole window, so they are not tied to this timestamp. |

The producer never waits for the reader and does not use an unbounded spin loop. Queue reservation has a fixed retry limit; a sample is rejected when the queue is full or that limit is exhausted. `ContentionEventListener.DroppedEventCount` exposes the cumulative rejected count. Durations of accepted samples accumulate as whole picoseconds; non-finite or negative durations contribute 0 while still being counted.

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
