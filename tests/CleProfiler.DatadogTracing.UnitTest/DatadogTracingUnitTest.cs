using ClrProfiler.DatadogTracing;

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
                    "gc_gen:2,gc_type:0,gc_reason:induced",
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
}
