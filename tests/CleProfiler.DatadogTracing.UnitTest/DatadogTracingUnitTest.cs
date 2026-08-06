using ClrProfiler.DatadogTracing;
using ClrProfiler.Statistics;
using StatsdClient;
// The test namespace shadows the adapter namespace, so alias the static class explicitly.
using Tracing = ClrProfiler.DatadogTracing.DatadogTracing;

namespace CleProfiler.DatadogTracing.UnitTest;

[NotInParallel]
public class DatadogTracingUnitTest
{
    [Test]
    public async Task DatadogTracingGCUniTest()
    {
        using var cts = new CancellationTokenSource();
        var logger = TestHelpers.CreateLogger<DatadogTracingUnitTest>();
        var host = "127.0.0.1";
        var port = 8125;
        var tag = "app:ClrProfiler.DatadogTracing.UnitTest";
        var complete = false;
        var list = new List<string>();

        // server
        var server = new TestHelpers.UdpServer(host, port)
        {
            OnRecieveMessage = (_, text) =>
            {
                if (!text.StartsWith("datadog.dogstatsd"))
                {
                    list.Add(text);
                }
            }
        };
        var serverTask = Task.Run(async () => await server.ListenAsync(cts.Token), cts.Token);

        // client
        var dogstatsdConfig = new StatsdConfig
        {
            StatsdServerName = host,
            StatsdPort = port,
            ConstantTags = new[] { tag },
        };
        DogStatsd.Configure(dogstatsdConfig);

        // enable clr tracker
        using var loggerFactory = TestHelpers.CreateLoggerFactory();
        var tracker = new ClrTracker(loggerFactory);
        tracker.EnableTracker();
        tracker.StartTracker();

        // Contention aggregates are emitted once per reader drain, which happens far more often than
        // statsd flushes, so the statsd type has to survive many submissions per interval. Emitted
        // here rather than in its own test because DogStatsd.Configure is process-global state.
        Tracing.ContentionEventStartEnd(new ContentionEventStatistics(1, 0, 3, 90D, 40D));

        // Allocate and GC
        while (!complete)
        {
            TestHelpers.Allocate5K();
            GC.Collect();
            await Task.Delay(10);

            if (list.Count >= 20)
            {
                complete = true;
            }
        }

        //await Assert.That(output).IsEqualTo("clr_diagnostics_event.gc.startend_count:18|c|#app:ConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced\nclr_diagnostics_event.gc.suspend_object_count:181|c|#app:ConsoleApp,gc_suspend_reason:gc\n");
        foreach (var item in list)
        {
            await Assert.That(item).Contains(tag);
        }
        await Assert.That(list).Contains(x => x.Contains("clr_diagnostics_event.gc.suspend_object_count"));
        await Assert.That(list).Contains(x => x.Contains("clr_diagnostics_event.gc.suspend_duration_ms"));
        await Assert.That(list).Contains(x => x.Contains("gc_gen:2,gc_type:0,gc_reason:induced"));
        await Assert.That(list).Contains(x => x.Contains("gc_suspend_reason:gc"));

        // Counters add up across submissions, so no aggregation window is lost.
        await Assert.That(list).Contains(x => x.Contains("clr_diagnostics_event.contention.startend_count:3|c|"));
        await Assert.That(list).Contains(x => x.Contains("clr_diagnostics_event.contention.startend_duration_ns_sum:90|c|"));
        // Histogram, not gauge: the agent keeps the maximum across every submission in the interval,
        // so per-window maxima compose into a true interval maximum. A gauge would keep only the last
        // window and discard the rest.
        await Assert.That(list).Contains(x => x.Contains("clr_diagnostics_event.contention.startend_duration_ns_max:40|h|"));
        await Assert.That(list).DoesNotContain(x => x.Contains("clr_diagnostics_event.contention.") && x.Contains("|g|"));

        tracker.StopTracker();
        cts.Cancel();
    }
}
