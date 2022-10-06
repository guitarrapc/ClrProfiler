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

## Mock on Localhost

Mock datadog via netcat running on container.

```bash
$ docker compose up

[+] Running 1/0
 - Container clrprofiler-datadog-mock-1  Created                                                                                                           0.0s
Attaching to clrprofiler-datadog-mock-1
```

Run Sandbox.

You will see metrics ingested to mock.

```
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.startend_count:8|c|#gc_gen:2,gc_type:0,gc_reason:induced
clrprofiler-datadog-mock-1  | datadog.dogstatsd.client.metrics:8|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.events:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.service_checks:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.bytes_sent:82|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.bytes_dropped:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.packets_sent:1|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.packets_dropped:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.packets_dropped_queue:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.aggregated_context_by_type:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udp,metrics_type:gaugedatadog.dogstatsd.client.aggregated_context_by_type:1|c|#client:csharp,client_version:7.0.0.0,client_transport:udp,metrics_type:countdatadog.dogstatsd.client.aggregated_context_by_type:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udp,metrics_type:setclr_diagnostics_event.gc.startend_duration_ms:1.1744|g|#gc_gen:2,gc_type:0,gc_reason:induced
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.suspend_object_count:1|c|#gc_suspend_reason:gc
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.startend_count:1|c|#gc_gen:2,gc_type:0,gc_reason:induced
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.suspend_duration_ms:0.9067000000000001|g|#gc_suspend_reason:gc
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.startend_duration_ms:0.852|g|#gc_gen:2,gc_type:0,gc_reason:induced
clrprofiler-datadog-mock-1  | datadog.dogstatsd.client.metrics:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.events:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.service_checks:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.bytes_sent:427|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.bytes_dropped:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.packets_sent:3|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.packets_dropped:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.packets_dropped_queue:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.aggregated_context_by_type:3|c|#client:csharp,client_version:7.0.0.0,client_transport:udp,metrics_type:gaugedatadog.dogstatsd.client.aggregated_context_by_type:2|c|#client:csharp,client_version:7.0.0.0,client_transport:udp,metrics_type:countdatadog.dogstatsd.client.aggregated_context_by_type:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udp,metrics_type:setdatadog.dogstatsd.client.metrics:6|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.events:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.service_checks:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.bytes_sent:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.bytes_dropped:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.packets_sent:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.packets_dropped:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.packets_dropped_queue:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udpdatadog.dogstatsd.client.aggregated_context_by_type:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udp,metrics_type:gaugedatadog.dogstatsd.client.aggregated_context_by_type:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udp,metrics_type:countdatadog.dogstatsd.client.aggregated_context_by_type:0|c|#client:csharp,client_version:7.0.0.0,client_transport:udp,metrics_type:setclr_diagnostics_event.gc.startend_count:6|c|#gc_gen:2,gc_type:0,gc_reason:induced
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.suspend_object_count:20|c|#gc_suspend_reason:gc
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.startend_duration_ms:0.6628|g|#gc_gen:2,gc_type:0,gc_reason:induced
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.suspend_duration_ms:1.7017|g|#gc_suspend_reason:gc
clrprofiler-datadog-mock-1  | clr_diagnostics_event.threadpool.available_workerthread_count:2|g
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.suspend_object_count:304|c|#gc_suspend_reason:gc
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.startend_count:19|c|#gc_gen:2,gc_type:0,gc_reason:induced
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.suspend_duration_ms:2.1077|g|#gc_suspend_reason:gc
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.startend_duration_ms:1.3904|g|#gc_gen:2,gc_type:0,gc_reason:induced
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.suspend_object_count:621|c|#gc_suspend_reason:gc
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.startend_count:18|c|#gc_gen:2,gc_type:0,gc_reason:induced
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.suspend_duration_ms:0.8049|g|#gc_suspend_reason:gc
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.startend_duration_ms:1.7825|g|#gc_gen:2,gc_type:0,gc_reason:induced
clrprofiler-datadog-mock-1  | clr_diagnostics_event.threadpool.adjustment_avg_throughput:0.00020668745385640035|g|#thread_adjust_reason:warmup
clrprofiler-datadog-mock-1  | clr_diagnostics_event.threadpool.adjustment_new_workerthreads_count:17|g|#thread_adjust_reason:warmup
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.suspend_object_count:1007|c|#gc_suspend_reason:gc
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.startend_count:19|c|#gc_gen:2,gc_type:0,gc_reason:induced
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.suspend_duration_ms:1.3779000000000001|g|#gc_suspend_reason:gc
clrprofiler-datadog-mock-1  | clr_diagnostics_event.gc.startend_duration_ms:2.9074|g|#gc_gen:2,gc_type:0,gc_reason:induced
```
