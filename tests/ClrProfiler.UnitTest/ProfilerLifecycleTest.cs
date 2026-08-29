using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;
using ClrProfiler.TimerListeners;

namespace ClrProfiler.UnitTest;

[NotInParallel]
public class ProfilerLifecycleTest
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task TrackerCreatesOnlyEnabledProfilers()
    {
        using var cts = new CancellationTokenSource();
        using var tracker = new ProfilerTracker(new ProfilerTrackerOptions
        {
            CancellationTokenSource = cts,
            EnabledFeatures = ProfilerFeature.GCEvent | ProfilerFeature.ThreadInfoTimer,
        });
        var names = new List<string>();

        tracker.Status(status => names.Add(status.Name));

        await Assert.That(names).IsEquivalentTo([
            nameof(GCEventProfiler),
            nameof(ThreadInfoTimerProfiler),
        ]);
    }

    [Test]
    public async Task TrackerEnablesAllProfilersByDefault()
    {
        using var cts = new CancellationTokenSource();
        using var tracker = new ProfilerTracker(new ProfilerTrackerOptions { CancellationTokenSource = cts });
        var names = new List<string>();

        tracker.Status(status => names.Add(status.Name));

        await Assert.That(names).IsEquivalentTo([
            nameof(GCEventProfiler),
            nameof(ThreadPoolEventProfiler),
            nameof(ContentionEventProfiler),
            nameof(ThreadInfoTimerProfiler),
            nameof(GCInfoTimerProfiler),
            nameof(ProcessInfoTimerProfiler),
            nameof(ProfilerDiagnosticsTimerProfiler),
        ]);
    }

    [Test]
    public async Task TrackerOwnsAdditionalProfilerLifecycle()
    {
        using var cts = new CancellationTokenSource();
        var profiler = new RecordingProfiler();
        var factoryCallCount = 0;
        using var tracker = new ProfilerTracker(new ProfilerTrackerOptions
        {
            CancellationTokenSource = cts,
            EnabledFeatures = ProfilerFeature.None,
            AdditionalProfilerFactories =
            [
                () =>
                {
                    factoryCallCount++;
                    return profiler;
                },
            ],
        });
        var names = new List<string>();

        tracker.Status(status => names.Add(status.Name));
        tracker.Start();
        tracker.Dispose();

        await Assert.That(factoryCallCount).IsEqualTo(1);
        await Assert.That(names).IsEquivalentTo([nameof(RecordingProfiler)]);
        await Assert.That(profiler.StartCount).IsEqualTo(1);
        await Assert.That(profiler.ReadCount).IsEqualTo(1);
        await Assert.That(profiler.StopCount).IsEqualTo(1);
        await Assert.That(profiler.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task TrackerDisposesCreatedProfilersWhenAdditionalFactoryThrows()
    {
        using var cts = new CancellationTokenSource();
        var profiler = new RecordingProfiler();
        var expectedException = new InvalidOperationException("factory failed");

        void CreateTracker() => _ = new ProfilerTracker(new ProfilerTrackerOptions
        {
            CancellationTokenSource = cts,
            EnabledFeatures = ProfilerFeature.None,
            AdditionalProfilerFactories =
            [
                () => profiler,
                () => throw expectedException,
            ],
        });

        await Assert.That(CreateTracker).Throws<InvalidOperationException>();
        await Assert.That(profiler.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task TrackerReportsNullAdditionalProfilerFactoryIndex()
    {
        using var cts = new CancellationTokenSource();
        var profiler = new RecordingProfiler();
        ArgumentException? actualException = null;

        try
        {
            _ = new ProfilerTracker(new ProfilerTrackerOptions
            {
                CancellationTokenSource = cts,
                EnabledFeatures = ProfilerFeature.None,
                AdditionalProfilerFactories =
                [
                    () => profiler,
                    null!,
                ],
            });
        }
        catch (ArgumentException ex)
        {
            actualException = ex;
        }

        await Assert.That(actualException).IsNotNull();
        await Assert.That(actualException!.Message).Contains("index 1");
        await Assert.That(profiler.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task TrackerReportsNullReturningAdditionalProfilerFactoryIndex()
    {
        using var cts = new CancellationTokenSource();
        var profiler = new RecordingProfiler();
        InvalidOperationException? actualException = null;

        try
        {
            _ = new ProfilerTracker(new ProfilerTrackerOptions
            {
                CancellationTokenSource = cts,
                EnabledFeatures = ProfilerFeature.None,
                AdditionalProfilerFactories =
                [
                    () => profiler,
                    () => null!,
                ],
            });
        }
        catch (InvalidOperationException ex)
        {
            actualException = ex;
        }

        await Assert.That(actualException).IsNotNull();
        await Assert.That(actualException!.Message).Contains("index 1");
        await Assert.That(profiler.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task TrackerLifecycleTransitionsAreIdempotent()
    {
        using var firstCts = new CancellationTokenSource();
        using var secondCts = new CancellationTokenSource();
        var profiler = new RecordingProfiler();
        using var tracker = new ProfilerTracker([profiler], new ProfilerTrackerOptions { CancellationTokenSource = firstCts });

        try
        {
            tracker.Start();
            tracker.Start();
            await Assert.That(profiler.StartCount).IsEqualTo(1);
            await Assert.That(profiler.ReadCount).IsEqualTo(1);

            tracker.Stop();
            tracker.Stop();
            await Assert.That(profiler.StopCount).IsEqualTo(1);

            tracker.Restart();
            tracker.Restart();
            await Assert.That(profiler.RestartCount).IsEqualTo(1);
            await Assert.That(profiler.ReadCount).IsEqualTo(1);

            tracker.Stop();
            tracker.Start();
            await Assert.That(profiler.RestartCount).IsEqualTo(2);
            await Assert.That(profiler.ReadCount).IsEqualTo(1);

            tracker.Cancel();
            tracker.Cancel();
            await Assert.That(profiler.StopCount).IsEqualTo(3);
            await Assert.That(firstCts.IsCancellationRequested).IsTrue();

            await Assert.That(tracker.Reset(secondCts)).IsTrue();
            tracker.Start();
            await Assert.That(profiler.StartCount).IsEqualTo(2);
            await Assert.That(profiler.ReadCount).IsEqualTo(2);
        }
        finally
        {
            tracker.Cancel();
        }
    }

    [Test]
    public async Task TrackersHaveIndependentStateAndCancellation()
    {
        using var firstCts = new CancellationTokenSource();
        using var secondCts = new CancellationTokenSource();
        var firstProfiler = new RecordingProfiler();
        var secondProfiler = new RecordingProfiler();
        using var firstTracker = new ProfilerTracker([firstProfiler], new ProfilerTrackerOptions { CancellationTokenSource = firstCts });
        using var secondTracker = new ProfilerTracker([secondProfiler], new ProfilerTrackerOptions { CancellationTokenSource = secondCts });

        firstTracker.Start();
        secondTracker.Start();
        firstTracker.Cancel();

        await Assert.That(firstCts.IsCancellationRequested).IsTrue();
        await Assert.That(secondCts.IsCancellationRequested).IsFalse();
        await Assert.That(firstProfiler.Enabled).IsFalse();
        await Assert.That(secondProfiler.Enabled).IsTrue();
        await Assert.That(firstProfiler.StartCount).IsEqualTo(1);
        await Assert.That(secondProfiler.StartCount).IsEqualTo(1);
    }

    [Test]
    public async Task TimerProfilersHaveIndependentTimers()
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        var firstSample = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSample = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timerOptions = (TimeSpan.FromMilliseconds(10), TimeSpan.FromDays(1));
        using var firstProfiler = new ThreadInfoTimerProfiler(_ =>
        {
            firstSample.TrySetResult();
            return Task.CompletedTask;
        }, exception => firstSample.TrySetException(exception), timerOptions);
        using var secondProfiler = new ThreadInfoTimerProfiler(_ =>
        {
            secondSample.TrySetResult();
            return Task.CompletedTask;
        }, exception => secondSample.TrySetException(exception), timerOptions);

        var firstReader = firstProfiler.ReadResultAsync(cts.Token);
        var secondReader = secondProfiler.ReadResultAsync(cts.Token);
        firstProfiler.Start();
        secondProfiler.Start();

        await Task.WhenAll(firstSample.Task, secondSample.Task).WaitAsync(TestTimeout);

        firstProfiler.Stop();
        secondProfiler.Stop();
        cts.Cancel();
        await Task.WhenAll(firstReader, secondReader);
    }

    [Test]
    public async Task DisposedTimerListenerCannotCreateAnotherTimer()
    {
        var listener = new TestableThreadInfoTimerListener(_ => Task.CompletedTask);

        listener.StartTimer();
        listener.Dispose();

        await Assert.That(listener.StartTimer).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task EventReaderContinuesAfterStopAndRestart()
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        var firstEvent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEvent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        using var listener = new TestableContentionEventListener(_ =>
        {
            if (Interlocked.Increment(ref count) == 1)
            {
                firstEvent.TrySetResult();
            }
            else
            {
                secondEvent.TrySetResult();
            }
            return Task.CompletedTask;
        });

        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        listener.ProcessEvent("ContentionStop_V1", DateTime.UtcNow, [0U, 0U, 1.0]);
        await firstEvent.Task.WaitAsync(TestTimeout);

        listener.Stop();
        listener.Restart();
        listener.ProcessEvent("ContentionStop_V1", DateTime.UtcNow, [1U, 0U, 2.0]);
        await secondEvent.Task.WaitAsync(TestTimeout);

        cts.Cancel();
        await readerTask;
        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task TimerReaderContinuesAfterStopAndRestart()
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        var firstSample = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSample = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        using var listener = new TestableThreadInfoTimerListener(_ =>
        {
            if (Interlocked.Increment(ref count) == 1)
            {
                firstSample.TrySetResult();
            }
            else
            {
                secondSample.TrySetResult();
            }
            return Task.CompletedTask;
        });

        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        listener.EventCreatedHandler();
        await firstSample.Task.WaitAsync(TestTimeout);

        listener.Stop();
        listener.Restart();
        listener.EventCreatedHandler();
        await secondSample.Task.WaitAsync(TestTimeout);

        cts.Cancel();
        await readerTask;
        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task EventReaderReportsCallbackExceptionAndContinues()
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        var expectedException = new InvalidOperationException("callback failed");
        var reportedException = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextEvent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        using var listener = new TestableContentionEventListener(_ =>
        {
            if (Interlocked.Increment(ref count) == 1)
            {
                return Task.FromException(expectedException);
            }
            nextEvent.TrySetResult();
            return Task.CompletedTask;
        }, exception => reportedException.TrySetResult(exception));

        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        listener.ProcessEvent("ContentionStop_V1", DateTime.UtcNow, [0U, 0U, 1.0]);
        listener.ProcessEvent("ContentionStop_V1", DateTime.UtcNow, [1U, 0U, 2.0]);

        await Assert.That(await reportedException.Task.WaitAsync(TestTimeout)).IsSameReferenceAs(expectedException);
        await nextEvent.Task.WaitAsync(TestTimeout);
        cts.Cancel();
        await readerTask;
        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task TimerReaderReportsCallbackExceptionAndContinues()
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        var expectedException = new InvalidOperationException("callback failed");
        var reportedException = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextSample = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        using var listener = new TestableThreadInfoTimerListener(_ =>
        {
            if (Interlocked.Increment(ref count) == 1)
            {
                return Task.FromException(expectedException);
            }
            nextSample.TrySetResult();
            return Task.CompletedTask;
        }, exception => reportedException.TrySetResult(exception));

        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        listener.EventCreatedHandler();
        listener.EventCreatedHandler();

        await Assert.That(await reportedException.Task.WaitAsync(TestTimeout)).IsSameReferenceAs(expectedException);
        await nextSample.Task.WaitAsync(TestTimeout);
        cts.Cancel();
        await readerTask;
        await Assert.That(count).IsEqualTo(2);
    }

    private sealed class RecordingProfiler : IProfiler
    {
        public string Name => nameof(RecordingProfiler);
        public bool Enabled { get; private set; }
        public int StartCount { get; private set; }
        public int RestartCount { get; private set; }
        public int StopCount { get; private set; }
        public int ReadCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void Start()
        {
            Enabled = true;
            StartCount++;
        }

        public void Restart()
        {
            Enabled = true;
            RestartCount++;
        }

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

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class TestableContentionEventListener(
        Func<ContentionEventStatistics, Task> onEventEmit,
        Action<Exception>? onEventError = null)
        : ContentionEventListener(onEventEmit, onEventError ?? (exception => throw exception))
    {
        public void EnableReading() => Enabled = true;
    }

    private sealed class TestableThreadInfoTimerListener(
        Func<ThreadInfoStatistics, Task> onEventEmit,
        Action<Exception>? onEventError = null)
        : ThreadInfoTimerListener(onEventEmit, onEventError ?? (exception => throw exception), TimeSpan.FromDays(1), TimeSpan.FromDays(1))
    {
        public void EnableReading() => Enabled = true;
        public void StartTimer() => RunWithCallback(EventCreatedHandler, static () => { });
    }
}
