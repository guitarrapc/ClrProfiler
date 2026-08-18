using ClrProfiler;
using ClrProfiler.DatadogTracing;

namespace CleProfiler.DatadogTracing.UnitTest;

[NotInParallel]
public class ClrTrackerMetricTagsInitializationTest
{
    [Test]
    public async Task EnableTracker_ManagesAdditionalProfilerLifecycle()
    {
        using var loggerFactory = TestHelpers.CreateLoggerFactory();
        var profiler = new RecordingProfiler();
        var factoryCallCount = 0;
        using var tracker = new ClrTracker(
            loggerFactory,
            new ClrTrackerOptions
            {
                TrackerType = ClrTrackerType.Logger,
                EnabledFeatures = ProfilerFeature.None,
                AdditionalProfilerFactories =
                [
                    () =>
                    {
                        factoryCallCount++;
                        return profiler;
                    },
                ],
            },
            static () => { });

        tracker.EnableTracker();
        tracker.StartTracker();
        var profilerCount = tracker.ProfilerCount;
        tracker.Dispose();

        await Assert.That(factoryCallCount).IsEqualTo(1);
        await Assert.That(profilerCount).IsEqualTo(1);
        await Assert.That(profiler.StartCount).IsEqualTo(1);
        await Assert.That(profiler.ReadCount).IsEqualTo(1);
        await Assert.That(profiler.StopCount).IsEqualTo(1);
        await Assert.That(profiler.DisposeCount).IsEqualTo(1);
    }

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

    private sealed class RecordingProfiler : IProfiler
    {
        public string Name => nameof(RecordingProfiler);
        public bool Enabled { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int ReadCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void Start()
        {
            Enabled = true;
            StartCount++;
        }

        public void Restart() => Enabled = true;

        public void Stop()
        {
            Enabled = false;
            StopCount++;
        }

        public Task ReadResultAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.CompletedTask;
        }

        public void Dispose() => DisposeCount++;
    }
}
