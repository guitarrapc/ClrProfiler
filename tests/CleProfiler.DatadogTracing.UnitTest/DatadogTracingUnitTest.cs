using ClrProfiler;
using ClrProfiler.DatadogTracing;
using ClrProfiler.Statistics;

namespace CleProfiler.DatadogTracing.UnitTest;

[NotInParallel(DogStatsdWireFixture.SerializationKey)]
public class DatadogTracingUnitTest
{
    private static readonly TimeSpan MetricTimeout = TimeSpan.FromSeconds(30);

    [Test]
    [ClassDataSource<DogStatsdWireFixture>(Shared = SharedType.PerTestSession)]
    public async Task GcEventMetrics_ReachTheAgentWithExpectedNamesAndTags(DogStatsdWireFixture wire)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var capture = wire.StartCapture();

        using var loggerFactory = TestHelpers.CreateLoggerFactory();
        using var tracker = new ClrTracker(loggerFactory);
        tracker.EnableTracker();
        tracker.StartTracker();

        string[] lines;
        try
        {
            // These metrics need real collections to happen, so keep allocating while waiting.
            lines = await capture.WaitForAllAsync(
                [
                    "clr_diagnostics_event.gc.suspend_object_count",
                    "clr_diagnostics_event.gc.suspend_duration_ms",
                    "clr_diagnostics_event.gc.heapstats_size_bytes",
                    "clr_diagnostics_event.gc.heapstats_gc_handle_count",
                    "clr_diagnostics_event.gc.global_count",
                    "clr_diagnostics_event.gc.global_memory_pressure",
                    "gc_gen:2,gc_type:0,gc_reason:induced",
                    "gc_gen:poh",
                    "gc_compaction:",
                    "gc_suspend_reason:gc",
                ],
                MetricTimeout,
                cancellationToken,
                static () =>
                {
                    TestHelpers.Allocate5K();
                    GC.Collect();
                    return Task.CompletedTask;
                });
        }
        finally
        {
            tracker.StopTracker();
        }

        foreach (var line in lines)
        {
            await Assert.That(line).Contains(DogStatsdWireFixture.ConstantTag);
        }
    }

    [Test]
    [ClassDataSource<DogStatsdWireFixture>(Shared = SharedType.PerTestSession)]
    public async Task ProfilerDiagnosticsMetric_ReachesTheAgentAsATaggedGauge(DogStatsdWireFixture wire)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var capture = wire.StartCapture();

        // Driven directly rather than through the tracker: the diagnostics timer runs on the shared
        // one-minute interval, which no wire test should wait for.
        var lines = await capture.WaitForAllAsync(
            [$"clr_diagnostics_timer.profiler.dropped_event_count:7|g|#{DogStatsdWireFixture.ConstantTag},profiler:{nameof(ContentionEventProfiler)}"],
            MetricTimeout,
            cancellationToken,
            static () =>
            {
                // Fully qualified: the test namespace itself ends in DatadogTracing.
                ClrProfiler.DatadogTracing.DatadogTracing.ProfilerDiagnosticsTimerGauge(
                    new ProfilerDiagnosticsStatistics(DateTime.UnixEpoch, nameof(ContentionEventProfiler), 7));
                return Task.CompletedTask;
            });

        await Assert.That(lines).IsNotEmpty();
    }
}
