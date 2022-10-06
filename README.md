# ClrProfiler

Library to gather .NET CLR Profiler for ContentionEvent, GC, Process, Thread and ThreadPool via EventListener.
Offering following Cloud tracing packages.

* Datadog

## Usage

Include `ClrProfiler.DatadogTracing` to your csproj and run following.
See detail sample on [Sandbox](/src/SandboxConsoleApp/Program.cs).

```cs
var tracker = new ClrTracker("localhost", logger);
tracker.StartTracker();
```

## Sandbox

Mock datadog agent (127.0.0.1:8125) via udp server running on sandbox itself.

Run Sandbox, and you will see metrics ingested to mock.

```
Enable tracking ClrTracker
Start tracking ClrTracker
Press Ctrl+C to cancel execution.
clr_diagnostics_event.gc.startend_count:18|c|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced
clr_diagnostics_event.gc.suspend_object_count:136|c|#app:SandboxConsoleApp,gc_suspend_reason:gc

clr_diagnostics_event.gc.startend_duration_ms:0.7066|g|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced
clr_diagnostics_event.gc.suspend_duration_ms:1.6307|g|#app:SandboxConsoleApp,gc_suspend_reason:gc

clr_diagnostics_event.gc.suspend_object_count:494|c|#app:SandboxConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_count:19|c|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_duration_ms:1.3336|g|#app:SandboxConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_duration_ms:1.9730999999999999|g|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_object_count:801|c|#app:SandboxConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_count:18|c|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced

clr_diagnostics_event.gc.suspend_duration_ms:0.7948999999999999|g|#app:SandboxConsoleApp,gc_suspend_reason:gc
clr_diagnostics_event.gc.startend_duration_ms:0.6851|g|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced
clr_diagnostics_event.threadpool.adjustment_avg_throughput:0.00019933169706460612|g|#app:SandboxConsoleApp,thread_adjust_reason:warmup
clr_diagnostics_event.threadpool.adjustment_new_workerthreads_count:17|g|#app:SandboxConsoleApp,thread_adjust_reason:warmup
```
