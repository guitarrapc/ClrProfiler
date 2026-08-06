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
        var originalOptions = ProfilerTracker.Options;
        using var firstCts = new CancellationTokenSource();
        using var secondCts = new CancellationTokenSource();
        var profiler = new RecordingProfiler();
        var tracker = new ProfilerTracker([profiler]);

        try
        {
            ProfilerTracker.Options = new ProfilerTrackerOptions { CancellationTokenSource = firstCts };

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
            ProfilerTracker.Options = originalOptions;
        }
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

    private sealed class TestableContentionEventListener(Func<ContentionEventStatistics, Task> onEventEmit)
        : ContentionEventListener(onEventEmit, exception => throw exception)
    {
        public void EnableReading() => Enabled = true;
    }

    private sealed class TestableThreadInfoTimerListener(Func<ThreadInfoStatistics, Task> onEventEmit)
        : ThreadInfoTimerListener(onEventEmit, exception => throw exception, TimeSpan.FromDays(1), TimeSpan.FromDays(1))
    {
        public void EnableReading() => Enabled = true;
    }
}
