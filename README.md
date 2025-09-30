# ClrProfiler

**ClrProfiler** is a zero-dependency .NET library designed to monitor and collect detailed metrics on Contention Events, Garbage Collection (GC), Processes, Threads, and ThreadPool activities through EventListener. This tool is essential for developers aiming to gain in-depth insights into the performance and behavior of their .NET applications.

## Key Features

- **Comprehensive Monitoring**
  ClrProfiler captures a wide range of CLR events, providing a holistic view of your application's runtime performance.
- **Cloud Tracing Integration**
  Seamlessly integrates with cloud tracing services, with built-in support for Datadog, enabling real-time monitoring and analytics.
- **Ease of Use**
  Designed for simplicity, ClrProfiler allows for straightforward integration into your projects, facilitating immediate performance tracking without the need for complex configurations.

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
var tracker = new ClrTracker(loggerFactory);
tracker.EnableTracker(); // required, enable clr tracker explicitly
tracker.StartTracker();
```

Now you are ready to use ClrTracker on your application. Metrics will be sent to Datadog by dogstatsd.

## Debugging

If you want debug behaviour, use ClrTrackerType.Logger instead. This will log metrics to ILogger.Debug.

```cs
// enable clr tracker
var tracker = new ClrTracker(loggerFactory, new ClrTrackerOptions
{
    TrackerType = ClrTrackerType.Logger
});
tracker.EnableTracker();
tracker.StartTracker();
```

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
var tracker = new ClrTracker(loggerFactory, new ClrTrackerOptions
{
    TrackerType = ClrTrackerType.Custom,
    CustomHandler = new MyCustomTrackerHandler(metricsService)
});
tracker.EnableTracker();
tracker.StartTracker();
```

This approach allows you to integrate ClrProfiler with any metrics backend or implement custom logic for processing CLR events.

## Sandbox

Run SandboxConsoleApp, then metrics ingested will shown on Console. Sandbox runs both Server and Client. Server is listen UDP Server on `127.0.0.1:8125` and accept request from local datadog agent.
You will see following messages.

```
clr_diagnostics_event.gc.startend_count:17|c|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced
clr_diagnostics_event.gc.suspend_object_count:120|c|#app:SandboxConsoleApp,gc_suspend_reason:gc

clr_diagnostics_event.gc.startend_duration_ms:2.4244|g|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced
clr_diagnostics_event.gc.suspend_duration_ms:2.8953|g|#app:SandboxConsoleApp,gc_suspend_reason:gc

clr_diagnostics_event.gc.suspend_object_count:475|c|#app:SandboxConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_count:19|c|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_duration_ms:0.9144|g|#app:SandboxConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_duration_ms:1.0946|g|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_object_count:783|c|#app:SandboxConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_count:18|c|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_duration_ms:2.7549|g|#app:SandboxConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_duration_ms:2.7791|g|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced
clr_diagnostics_event.threadpool.adjustment_avg_throughput:0.00017109293075546954|g|#app:SandboxConsoleApp,thread_adjust_reason:warmup
clr_diagnostics_event.threadpool.adjustment_new_workerthreads_count:17|g|#app:SandboxConsoleApp,thread_adjust_reason:warmup

clr_diagnostics_event.gc.suspend_object_count:1178|c|#app:SandboxConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_count:19|c|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_duration_ms:0.7547999999999999|g|#app:SandboxConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_duration_ms:2.6473|g|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

datadog.dogstatsd.client.metrics:362|c|#app:SandboxConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:SandboxConsoleApp
datadog.dogstatsd.client.events:0|c|#app:SandboxConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:SandboxConsoleApp
datadog.dogstatsd.client.service_checks:0|c|#app:SandboxConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:SandboxConsoleApp
datadog.dogstatsd.client.bytes_sent:1928|c|#app:SandboxConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:SandboxConsoleApp
datadog.dogstatsd.client.bytes_dropped:0|c|#app:SandboxConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:SandboxConsoleApp
datadog.dogstatsd.client.packets_sent:8|c|#app:SandboxConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:SandboxConsoleApp
datadog.dogstatsd.client.packets_dropped:0|c|#app:SandboxConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:SandboxConsoleApp
datadog.dogstatsd.client.packets_dropped_queue:0|c|#app:SandboxConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:SandboxConsoleApp
datadog.dogstatsd.client.aggregated_context_by_type:10|c|#app:SandboxConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:SandboxConsoleApp,metrics_type:gauge
datadog.dogstatsd.client.aggregated_context_by_type:8|c|#app:SandboxConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:SandboxConsoleApp,metrics_type:count
datadog.dogstatsd.client.aggregated_context_by_type:0|c|#app:SandboxConsoleApp,client:csharp,client_version:7.0.0.0,client_transport:udp,app:SandboxConsoleApp,metrics_type:set
clr_diagnostics_event.gc.suspend_object_count:1539|c|#app:SandboxConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_count:19|c|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_duration_ms:2.0896|g|#app:SandboxConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_duration_ms:2.8951|g|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced
```
