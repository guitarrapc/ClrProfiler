using ClrProfiler.Statistics;
using ClrProfiler.TimerListeners;

namespace ClrProfiler.UnitTest;

/// <summary>
/// Covers the self-observability path: every profiler reports how many events it discarded, and the
/// diagnostics timer projects those counts as ordinary statistics so an adapter can export them.
/// </summary>
[NotInParallel]
public class ProfilerDiagnosticsTest
{
    private const int ChannelCapacity = 50;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UnusedTimerPeriod = TimeSpan.FromDays(1);

    [Test]
    public async Task ProfilerDroppedEventCountDefaultsToZeroForCustomProfilers()
    {
        // A profiler written before this member existed must keep compiling and report no loss.
        using IProfiler profiler = new UninstrumentedProfiler();

        await Assert.That(profiler.DroppedEventCount).IsEqualTo(0L);
    }

    [Test]
    public async Task BuiltInProfilersExposeTheirListenerDroppedEventCount()
    {
        using var gc = new GCEventProfiler(static _ => Task.CompletedTask, static _ => { });
        using var threadPool = new ThreadPoolEventProfiler(static _ => Task.CompletedTask, static _ => { });
        using var contention = new ContentionEventProfiler(static _ => Task.CompletedTask, static _ => { });
        using var threadInfo = new ThreadInfoTimerProfiler(static _ => Task.CompletedTask, static _ => { }, (UnusedTimerPeriod, UnusedTimerPeriod));
        using var gcInfo = new GCInfoTimerProfiler(static _ => Task.CompletedTask, static _ => { }, (UnusedTimerPeriod, UnusedTimerPeriod));
        using var processInfo = new ProcessInfoTimerProfiler(static _ => Task.CompletedTask, static _ => { }, (UnusedTimerPeriod, UnusedTimerPeriod));

        await Assert.That(gc.DroppedEventCount).IsEqualTo(0L);
        await Assert.That(threadPool.DroppedEventCount).IsEqualTo(0L);
        await Assert.That(contention.DroppedEventCount).IsEqualTo(0L);
        await Assert.That(threadInfo.DroppedEventCount).IsEqualTo(0L);
        await Assert.That(gcInfo.DroppedEventCount).IsEqualTo(0L);
        await Assert.That(processInfo.DroppedEventCount).IsEqualTo(0L);
    }

    [Test]
    public async Task TimerListenerEmitsOneSamplePerObservedProfiler()
    {
        var actual = new List<ProfilerDiagnosticsStatistics>();
        using var cts = new CancellationTokenSource(TestTimeout);
        IProfiler[] profilers =
        [
            new MinimalProfiler("first", 3),
            new MinimalProfiler("second", 0),
            new MinimalProfiler("third", 41),
        ];
        using var listener = new TestableProfilerDiagnosticsTimerListener(profilers, value =>
        {
            actual.Add(value);
            if (actual.Count == profilers.Length)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });

        listener.EventCreatedHandler();
        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        await Assert.That(actual).Count().IsEqualTo(3);
        await Assert.That(actual[0].ProfilerName).IsEqualTo("first");
        await Assert.That(actual[0].DroppedEventCount).IsEqualTo(3L);
        await Assert.That(actual[1].ProfilerName).IsEqualTo("second");
        await Assert.That(actual[1].DroppedEventCount).IsEqualTo(0L);
        await Assert.That(actual[2].ProfilerName).IsEqualTo("third");
        await Assert.That(actual[2].DroppedEventCount).IsEqualTo(41L);
        foreach (var value in actual)
        {
            await Assert.That(value.Date > DateTime.MinValue).IsTrue();
        }
    }

    [Test]
    public async Task TimerListenerReportsTheLatestCumulativeCountOnEveryTick()
    {
        var actual = new List<ProfilerDiagnosticsStatistics>();
        using var cts = new CancellationTokenSource(TestTimeout);
        var profiler = new MinimalProfiler("only", 1);
        using var listener = new TestableProfilerDiagnosticsTimerListener([profiler], value =>
        {
            actual.Add(value);
            if (actual.Count == 2)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });

        listener.EventCreatedHandler();
        profiler.DroppedEventCount = 9;
        listener.EventCreatedHandler();
        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        await Assert.That(actual).Count().IsEqualTo(2);
        await Assert.That(actual[0].DroppedEventCount).IsEqualTo(1L);
        await Assert.That(actual[1].DroppedEventCount).IsEqualTo(9L);
    }

    [Test]
    public async Task TimerListenerSkipsProfilerSlotsThatAreNotPopulatedYet()
    {
        var actual = new List<ProfilerDiagnosticsStatistics>();
        using var cts = new CancellationTokenSource(TestTimeout);
        // The tracker hands its own backing array to the listener before every slot is filled, so a
        // tick that races construction must report the populated profilers instead of throwing.
        var profilers = new IProfiler?[] { new MinimalProfiler("present", 7), null };
        using var listener = new TestableProfilerDiagnosticsTimerListener(profilers, value =>
        {
            actual.Add(value);
            cts.Cancel();
            return Task.CompletedTask;
        });

        listener.EventCreatedHandler();
        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        var single = await Assert.That(actual).HasSingleItem();
        await Assert.That(single.ProfilerName).IsEqualTo("present");
        await Assert.That(single.DroppedEventCount).IsEqualTo(7L);
    }

    [Test]
    public async Task TimerListenerReportsSamplesDroppedBeyondChannelCapacity()
    {
        var actual = new List<ProfilerDiagnosticsStatistics>();
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableProfilerDiagnosticsTimerListener([new MinimalProfiler("only", 0)], value =>
        {
            actual.Add(value);
            if (actual.Count == ChannelCapacity)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });

        for (var i = 0; i < ChannelCapacity; i++)
        {
            listener.EventCreatedHandler();
        }
        await Assert.That(listener.DroppedEventCount).IsEqualTo(0L);

        listener.EventCreatedHandler();

        await Assert.That(listener.DroppedEventCount).IsEqualTo(1L);
        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);
        await Assert.That(actual).Count().IsEqualTo(ChannelCapacity);
    }

    [Test]
    public async Task TimerListenerReportsCallbackExceptionAndKeepsReading()
    {
        var expectedException = new InvalidOperationException("emit failed");
        var reportedException = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSample = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TestTimeout);
        var count = 0;
        using var listener = new TestableProfilerDiagnosticsTimerListener(
            [new MinimalProfiler("only", 0)],
            _ =>
            {
                count++;
                if (count == 1)
                {
                    throw expectedException;
                }
                secondSample.TrySetResult();
                return Task.CompletedTask;
            },
            exception => reportedException.TrySetResult(exception));

        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        listener.EventCreatedHandler();
        listener.EventCreatedHandler();

        await Assert.That(await reportedException.Task.WaitAsync(TestTimeout)).IsSameReferenceAs(expectedException);
        await secondSample.Task.WaitAsync(TestTimeout);
        cts.Cancel();
        await readerTask;
        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task DisposedTimerListenerDoesNotRecreateItsTimer()
    {
        var listener = new TestableProfilerDiagnosticsTimerListener([new MinimalProfiler("only", 0)], static _ => Task.CompletedTask);
        listener.Dispose();

        await Assert.That(listener.StartTimer).Throws<ObjectDisposedException>();
        await Assert.That(listener.Enabled).IsFalse();
    }

    [Test]
    public async Task TrackerObservesEveryProfilerItOwnsIncludingAdditionalOnes()
    {
        var samples = new List<ProfilerDiagnosticsStatistics>();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var additional = new MinimalProfiler("custom", 12);
        using var tracker = new ProfilerTracker(new ProfilerTrackerOptions
        {
            CancellationTokenSource = cts,
            EnabledFeatures = ProfilerFeature.GCEvent | ProfilerFeature.ProfilerDiagnosticsTimer,
            TimerOption = (TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)),
            AdditionalProfilerFactories = [() => additional],
            ProfilerDiagnosticsTimerCallback = (statistics =>
            {
                lock (samples)
                {
                    samples.Add(statistics);
                }
                if (statistics.ProfilerName == nameof(ProfilerDiagnosticsTimerProfiler))
                {
                    observed.TrySetResult();
                }
                return Task.CompletedTask;
            }, _ => { }),
        });

        tracker.Start();
        await observed.Task.WaitAsync(TestTimeout);
        tracker.Cancel();

        string[] names;
        lock (samples)
        {
            names = [.. samples.Select(sample => sample.ProfilerName).Distinct()];
        }
        await Assert.That(names).Contains(nameof(GCEventProfiler));
        await Assert.That(names).Contains("custom");
        await Assert.That(names).Contains(nameof(ProfilerDiagnosticsTimerProfiler));
    }

    [Test]
    public async Task TrackerDoesNotCreateTheDiagnosticsProfilerWhenTheFeatureIsOff()
    {
        using var cts = new CancellationTokenSource();
        using var tracker = new ProfilerTracker(new ProfilerTrackerOptions
        {
            CancellationTokenSource = cts,
            EnabledFeatures = ProfilerFeature.All & ~ProfilerFeature.ProfilerDiagnosticsTimer,
        });
        var names = new List<string>();

        tracker.Status(status => names.Add(status.Name));

        await Assert.That(names).DoesNotContain(nameof(ProfilerDiagnosticsTimerProfiler));
    }

    private sealed class MinimalProfiler(string name = nameof(MinimalProfiler), long droppedEventCount = 0) : IProfiler
    {
        public string Name { get; } = name;
        public bool Enabled { get; private set; }
        public long DroppedEventCount { get; set; } = droppedEventCount;

        public void Start() => Enabled = true;
        public void Restart() => Enabled = true;
        public void Stop() => Enabled = false;
        public Task ReadResultAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => Enabled = false;
    }

    /// <summary>Implements only the members that existed before drop reporting was added.</summary>
    private sealed class UninstrumentedProfiler : IProfiler
    {
        public string Name => nameof(UninstrumentedProfiler);
        public bool Enabled { get; private set; }

        public void Start() => Enabled = true;
        public void Restart() => Enabled = true;
        public void Stop() => Enabled = false;
        public Task ReadResultAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => Enabled = false;
    }

    private sealed class TestableProfilerDiagnosticsTimerListener(
        IReadOnlyList<IProfiler?> profilers,
        Func<ProfilerDiagnosticsStatistics, Task> onEventEmit,
        Action<Exception>? onEventError = null)
        : ProfilerDiagnosticsTimerListener(onEventEmit, onEventError ?? (exception => throw exception), UnusedTimerPeriod, UnusedTimerPeriod, profilers)
    {
        public void EnableReading() => Enabled = true;
        public void StartTimer() => RunWithCallback(EventCreatedHandler, static () => { });
    }
}
