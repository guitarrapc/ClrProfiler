using ClrProfiler.DatadogTracing;

namespace CleProfiler.DatadogTracing.UnitTest;

[NotInParallel]
public class ClrTrackerMetricTagsInitializationTest
{
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
