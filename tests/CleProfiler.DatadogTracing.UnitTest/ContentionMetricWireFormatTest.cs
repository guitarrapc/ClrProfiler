using ClrProfiler.Statistics;
// The test namespace shadows the adapter namespace, so alias the static class explicitly.
using Tracing = ClrProfiler.DatadogTracing.DatadogTracing;

namespace CleProfiler.DatadogTracing.UnitTest;

/// <summary>
/// Guards the statsd metric type of every contention metric. The type decides how a value is
/// aggregated over a flush interval, and the listener emits an aggregation window every time the
/// reader drains, which is far more often than statsd flushes. A type that keeps only one
/// submission per interval therefore discards almost every window.
/// </summary>
[NotInParallel(DogStatsdWireFixture.SerializationKey)]
public class ContentionMetricWireFormatTest
{
    private static readonly TimeSpan MetricTimeout = TimeSpan.FromSeconds(30);

    [Test]
    [ClassDataSource<DogStatsdWireFixture>(Shared = SharedType.PerTestSession)]
    public async Task ContentionEventStartEnd_EmitsTypesThatSurviveIntervalAggregation(DogStatsdWireFixture wire)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var capture = wire.StartCapture();

        Tracing.ContentionEventStartEnd(new ContentionEventStatistics(1, 0, 3, 90D, 40D));

        var lines = await capture.WaitForAllAsync(
            [
                // Counters add up across submissions, so no aggregation window is lost.
                "clr_diagnostics_event.contention.startend_count:3|c|",
                "clr_diagnostics_event.contention.startend_duration_ns_sum:90|c|",
                // Histogram, not gauge: the agent keeps the maximum across every submission in the
                // interval, so per-window maxima compose into a true interval maximum.
                "clr_diagnostics_event.contention.startend_duration_ns_max:40|h|",
            ],
            MetricTimeout,
            cancellationToken);

        var contentionLines = lines
            .Where(x => x.Contains("clr_diagnostics_event.contention.", StringComparison.Ordinal))
            .ToArray();

        // A gauge would keep only the last window in a flush interval and discard the rest.
        await Assert.That(contentionLines).DoesNotContain(x => x.Contains("|g|", StringComparison.Ordinal));
        foreach (var line in contentionLines)
        {
            await Assert.That(line).Contains(DogStatsdWireFixture.ConstantTag);
        }
    }
}
