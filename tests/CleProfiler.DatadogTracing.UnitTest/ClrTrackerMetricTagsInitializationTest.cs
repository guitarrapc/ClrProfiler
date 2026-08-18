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
        tracker.EnableTracker();

        await Assert.That(tracker.ProfilerCount).IsEqualTo(1);
    }

    [Test]
    public async Task ProfilerCount_AfterDispose_ThrowsObjectDisposedException()
    {
        using var loggerFactory = TestHelpers.CreateLoggerFactory();
        var tracker = new ClrTracker(
            loggerFactory,
            new ClrTrackerOptions { TrackerType = ClrTrackerType.Logger },
            static () => { });

        tracker.EnableTracker();
        tracker.Dispose();

        await Assert.That(() => tracker.ProfilerCount).Throws<ObjectDisposedException>();
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
