using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;
using ClrProfiler.TimerListeners;

namespace ClrProfiler.UnitTest;

[Collection(nameof(TestCollectionDefinition))]
public class ProfilerLifecycleTest
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void TrackerLifecycleTransitionsAreIdempotent()
    {
        using var firstCts = new CancellationTokenSource();
        using var secondCts = new CancellationTokenSource();
        var profiler = new RecordingProfiler();
        using var tracker = new ProfilerTracker([profiler], new ProfilerTrackerOptions { CancellationTokenSource = firstCts });

        try
        {
            tracker.Start();
            tracker.Start();
            Assert.Equal(1, profiler.StartCount);
            Assert.Equal(1, profiler.ReadCount);

            tracker.Stop();
            tracker.Stop();
            Assert.Equal(1, profiler.StopCount);

            tracker.Restart();
            tracker.Restart();
            Assert.Equal(1, profiler.RestartCount);
            Assert.Equal(1, profiler.ReadCount);

            tracker.Stop();
            tracker.Start();
            Assert.Equal(2, profiler.RestartCount);
            Assert.Equal(1, profiler.ReadCount);

            tracker.Cancel();
            tracker.Cancel();
            Assert.Equal(3, profiler.StopCount);
            Assert.True(firstCts.IsCancellationRequested);

            Assert.True(tracker.Reset(secondCts));
            tracker.Start();
            Assert.Equal(2, profiler.StartCount);
            Assert.Equal(2, profiler.ReadCount);
        }
        finally
        {
            tracker.Cancel();
        }
    }

    [Fact]
    public void TrackersHaveIndependentStateAndCancellation()
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

        Assert.True(firstCts.IsCancellationRequested);
        Assert.False(secondCts.IsCancellationRequested);
        Assert.False(firstProfiler.Enabled);
        Assert.True(secondProfiler.Enabled);
        Assert.Equal(1, firstProfiler.StartCount);
        Assert.Equal(1, secondProfiler.StartCount);
    }

    [Fact]
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

    [Fact]
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
        Assert.Equal(2, count);
    }

    [Fact]
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
        Assert.Equal(2, count);
    }

    [Fact]
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

        Assert.Same(expectedException, await reportedException.Task.WaitAsync(TestTimeout));
        await nextEvent.Task.WaitAsync(TestTimeout);
        cts.Cancel();
        await readerTask;
        Assert.Equal(2, count);
    }

    [Fact]
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

        Assert.Same(expectedException, await reportedException.Task.WaitAsync(TestTimeout));
        await nextSample.Task.WaitAsync(TestTimeout);
        cts.Cancel();
        await readerTask;
        Assert.Equal(2, count);
    }

    private sealed class RecordingProfiler : IProfiler
    {
        public string Name => nameof(RecordingProfiler);
        public bool Enabled { get; private set; }
        public int StartCount { get; private set; }
        public int RestartCount { get; private set; }
        public int StopCount { get; private set; }
        public int ReadCount { get; private set; }

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
    }
}
