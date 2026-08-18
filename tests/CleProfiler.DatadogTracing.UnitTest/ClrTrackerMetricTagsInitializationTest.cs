using ClrProfiler;
using ClrProfiler.DatadogTracing;

namespace CleProfiler.DatadogTracing.UnitTest;

[NotInParallel]
public class ClrTrackerMetricTagsInitializationTest
{
    [Test]
    public async Task EnableTracker_ForwardsEnabledFeaturesToProfilerTracker()
    {
        using var loggerFactory = TestHelpers.CreateLoggerFactory();
        using var tracker = new ClrTracker(
            loggerFactory,
            new ClrTrackerOptions
            {
                TrackerType = ClrTrackerType.Logger,
                EnabledFeatures = ProfilerFeature.ThreadPoolEvent,
            },
            static () => { });
        var names = new List<string>();

        tracker.EnableTracker();
        tracker.Status(status => names.Add(status.Name));

        await Assert.That(names).IsEquivalentTo([nameof(ThreadPoolEventProfiler)]);
    }

    [Test]
    [Arguments(ClrTrackerType.Datadog)]
    [Arguments(ClrTrackerType.Logger)]
    public async Task EnableTracker_PrewarmsMetricTagsExactlyOnce(ClrTrackerType trackerType)
    {
        using var loggerFactory = TestHelpers.CreateLoggerFactory();
        var initializationCount = 0;
        using var tracker = new ClrTracker(
            loggerFactory,
            new ClrTrackerOptions { TrackerType = trackerType },
            () => initializationCount++);

        tracker.EnableTracker();
        tracker.EnableTracker();

        await Assert.That(initializationCount).IsEqualTo(1);
    }
}
