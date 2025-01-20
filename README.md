# ClrProfiler

Library to gather .NET CLR Profiler for ContentionEvent, GC, Process, Thread and ThreadPool via EventListener.
Offering following Cloud tracing packages.

* [x] Datadog

## Basic Usage

Include `ClrProfiler.DatadogTracing` to your csproj and run following.

By default, ClrProfiler will start with `Datadog` tracer.

```cs
// Run Client (datadog agent with udp)
var dogstatsdConfig = new StatsdConfig
{
    StatsdServerName = host,
    StatsdPort = port,
    ConstantTags = [$"app:SandboxConsoleApp"],
};
DogStatsd.Configure(dogstatsdConfig);

// enable clr tracker
var tracker = new ClrTracker(loggerFactory);
tracker.EnableTracker(); // EnableTracker will start EventListener
tracker.StartTracker();
```

If you want to debug by logger, use ClrTrackerType.Logger.

```cs
// enable clr tracker
var tracker = new ClrTracker(loggerFactory, new ClrTrackerOptions
{
    TrackerType = ClrTrackerType.Logger
});
tracker.EnableTracker();
tracker.StartTracker();
```

## Sandbox

Run SandboxConsoleApp, then metrics ingested will show on Console.

Sandbox will run both Server and Client. Server is listen on `127.0.0.1:8125` and accept udp from local datadog agent.
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
