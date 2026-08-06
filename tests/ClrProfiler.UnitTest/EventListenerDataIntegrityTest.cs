using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;

namespace ClrProfiler.UnitTest;

[Collection(nameof(TestCollectionDefinition))]
public class EventListenerDataIntegrityTest
{
    private const int ChannelCapacity = 50;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task GCEventListenerPreservesEveryEventAtChannelCapacity()
    {
        var actual = new List<GCEventStatistics>(ChannelCapacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        TestableGCEventListener? listener = null;
        listener = new TestableGCEventListener(value =>
        {
            actual.Add(value);
            if (actual.Count == ChannelCapacity)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });
        using (listener)
        {
            var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < ChannelCapacity / 2; i++)
            {
                var gcStart = origin.AddTicks(i * 10_000L);
                var gcEnd = gcStart.AddTicks(5_000);
                listener.ProcessEvent("GCStart_V2", gcStart, [(uint)i, 0U, (uint)(i % 3), (uint)(i % 2)]);
                listener.ProcessEvent("GCEnd_V1", gcEnd, [(uint)i, (uint)(i % 3)]);

                var suspendStart = gcEnd.AddTicks(1_000);
                var suspendEnd = suspendStart.AddTicks(2_500);
                listener.ProcessEvent("GCSuspendEEBegin_V1", suspendStart, [1U, (uint)(100 + i)]);
                listener.ProcessEvent("GCRestartEEEnd_V1", suspendEnd, []);
            }

            listener.EnableReading();
            await listener.OnReadResultAsync(cts.Token);
        }

        Assert.Equal(ChannelCapacity, actual.Count);
        for (var i = 0; i < ChannelCapacity / 2; i++)
        {
            var startEnd = actual[i * 2];
            Assert.Equal(GCEventType.GCStartEnd, startEnd.Type);
            Assert.Equal((uint)i, startEnd.GCStartEndStatistics.Index);
            Assert.Equal((uint)(i % 2), startEnd.GCStartEndStatistics.Type);
            Assert.Equal((uint)(i % 3), startEnd.GCStartEndStatistics.Generation);
            Assert.Equal((uint)(i % 3), startEnd.GCStartEndStatistics.Reason);
            Assert.Equal(0.5, startEnd.GCStartEndStatistics.DurationMillsec);

            var suspend = actual[(i * 2) + 1];
            Assert.Equal(GCEventType.GCSuspend, suspend.Type);
            Assert.Equal(1U, suspend.GCSuspendStatistics.Reason);
            Assert.Equal((uint)(100 + i), suspend.GCSuspendStatistics.Count);
            Assert.Equal(0.25, suspend.GCSuspendStatistics.DurationMillisec);
        }
    }

    [Fact]
    public async Task GCEventListenerCorrelatesOverlappingCollectionsByIndex()
    {
        var actual = new List<GCEventStatistics>(2);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCEventListener(value =>
        {
            actual.Add(value);
            if (actual.Count == 2)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        listener.ProcessEvent("GCStart_V2", origin, [100U, 2U, 4U, 1U]);
        listener.ProcessEvent("GCStart_V2", origin.AddTicks(10_000), [101U, 0U, 0U, 0U]);
        listener.ProcessEvent("GCEnd_V1", origin.AddTicks(15_000), [101U, 0U]);
        listener.ProcessEvent("GCEnd_V1", origin.AddTicks(50_000), [100U, 2U]);

        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        Assert.Equal(2, actual.Count);

        var foreground = actual[0].GCStartEndStatistics;
        Assert.Equal(101U, foreground.Index);
        Assert.Equal(0U, foreground.Type);
        Assert.Equal(0U, foreground.Generation);
        Assert.Equal(0U, foreground.Reason);
        Assert.Equal(origin.AddTicks(10_000).Ticks, foreground.GCStartTime);
        Assert.Equal(0.5, foreground.DurationMillsec);

        var background = actual[1].GCStartEndStatistics;
        Assert.Equal(100U, background.Index);
        Assert.Equal(1U, background.Type);
        Assert.Equal(2U, background.Generation);
        Assert.Equal(4U, background.Reason);
        Assert.Equal(origin.Ticks, background.GCStartTime);
        Assert.Equal(5.0, background.DurationMillsec);
    }

    [Fact]
    public async Task GCEventListenerDoesNotLoseLongRunningBackgroundGCOnIndexCollision()
    {
        var actual = new List<GCEventStatistics>(2);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCEventListener(value =>
        {
            actual.Add(value);
            if (actual.Count == 2)
            {
                completed.TrySetResult();
            }
            return Task.CompletedTask;
        });

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        try
        {
            listener.ProcessEvent("GCStart_V2", origin, [0U, 2U, 4U, 1U]);
            listener.ProcessEvent("GCStart_V2", origin.AddTicks(10_000), [64U, 0U, 0U, 0U]);
            listener.ProcessEvent("GCEnd_V1", origin.AddTicks(15_000), [64U, 0U]);
            listener.ProcessEvent("GCEnd_V1", origin.AddTicks(50_000), [0U, 2U]);

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        Assert.Equal([64U, 0U], actual.Select(x => x.GCStartEndStatistics.Index));
        Assert.Equal(0.5, actual[0].GCStartEndStatistics.DurationMillsec);
        Assert.Equal(5.0, actual[1].GCStartEndStatistics.DurationMillsec);
    }

    [Fact]
    public async Task GCEventListenerCorrelatesCollidingIndicesFromConcurrentWriters()
    {
        var actual = new List<GCEventStatistics>(2);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCEventListener(value =>
        {
            actual.Add(value);
            if (actual.Count == 2)
            {
                completed.TrySetResult();
            }
            return Task.CompletedTask;
        });
        using var startBarrier = new Barrier(2);
        using var endBarrier = new Barrier(2);

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        try
        {
            await Task.WhenAll(
                Task.Run(() =>
                {
                    startBarrier.SignalAndWait(TestContext.Current.CancellationToken);
                    listener.ProcessEvent("GCStart_V2", origin, [0U, 2U, 4U, 1U]);
                }, TestContext.Current.CancellationToken),
                Task.Run(() =>
                {
                    startBarrier.SignalAndWait(TestContext.Current.CancellationToken);
                    listener.ProcessEvent("GCStart_V2", origin.AddTicks(10_000), [64U, 0U, 0U, 0U]);
                }, TestContext.Current.CancellationToken));

            await Task.WhenAll(
                Task.Run(() =>
                {
                    endBarrier.SignalAndWait(TestContext.Current.CancellationToken);
                    listener.ProcessEvent("GCEnd_V1", origin.AddTicks(50_000), [0U, 2U]);
                }, TestContext.Current.CancellationToken),
                Task.Run(() =>
                {
                    endBarrier.SignalAndWait(TestContext.Current.CancellationToken);
                    listener.ProcessEvent("GCEnd_V1", origin.AddTicks(15_000), [64U, 0U]);
                }, TestContext.Current.CancellationToken));

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        Assert.Equal([0U, 64U], actual.Select(x => x.GCStartEndStatistics.Index).Order());
    }

    [Fact]
    public async Task GCEventListenerMalformedEndDoesNotConsumeValidStart()
    {
        var actual = new List<GCEventStatistics>(1);
        var errors = new List<Exception>(1);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCEventListener(value =>
        {
            actual.Add(value);
            completed.TrySetResult();
            return Task.CompletedTask;
        }, errors.Add);

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        try
        {
            listener.ProcessEvent("GCStart_V2", origin, [0U, 2U, 4U, 1U]);
            listener.ProcessEvent("GCEnd_V1", origin.AddTicks(1_000), []);
            listener.ProcessEvent("GCEnd_V1", origin.AddTicks(50_000), [0U, 2U]);

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var result = Assert.Single(actual).GCStartEndStatistics;
        Assert.Equal(0U, result.Index);
        Assert.Equal(origin.AddTicks(50_000).Ticks, result.GCEndTime);
        Assert.Equal(5.0, result.DurationMillsec);
        Assert.Single(errors);
    }

    [Fact]
    public async Task GCEventListenerMalformedStartDoesNotReplaceValidStart()
    {
        var actual = new List<GCEventStatistics>(1);
        var errors = new List<Exception>(1);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCEventListener(value =>
        {
            actual.Add(value);
            completed.TrySetResult();
            return Task.CompletedTask;
        }, errors.Add);

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        try
        {
            listener.ProcessEvent("GCStart_V2", origin, [0U, 2U, 4U, 1U]);
            listener.ProcessEvent("GCStart_V2", origin.AddTicks(1_000), []);
            listener.ProcessEvent("GCEnd_V1", origin.AddTicks(50_000), [0U, 2U]);

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var result = Assert.Single(actual).GCStartEndStatistics;
        Assert.Equal(origin.Ticks, result.GCStartTime);
        Assert.Equal(5.0, result.DurationMillsec);
        Assert.Single(errors);
    }

    [Fact]
    public async Task GCEventListenerAcceptsSupportedIntegerPayloadRepresentations()
    {
        var actual = new List<GCEventStatistics>(1);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCEventListener(value =>
        {
            actual.Add(value);
            completed.TrySetResult();
            return Task.CompletedTask;
        });

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        try
        {
            listener.ProcessEvent("GCStart_V2", origin, [1, (short)2, "4", (byte)1]);
            listener.ProcessEvent("GCEnd_V1", origin.AddTicks(5_000), [1L, (ushort)2]);

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var result = Assert.Single(actual).GCStartEndStatistics;
        Assert.Equal(1U, result.Index);
        Assert.Equal(1U, result.Type);
        Assert.Equal(2U, result.Generation);
        Assert.Equal(4U, result.Reason);
        Assert.Equal(0.5, result.DurationMillsec);
    }

    [Fact]
    public void GCEventListenerNormalStartEndHotPathDoesNotAllocate()
    {
        using var listener = new TestableGCEventListener(_ => Task.CompletedTask);
        object?[] startPayload = [0U, 2U, 4U, 1U];
        object?[] endPayload = [0U, 2U];
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddTicks(5_000);

        for (var i = 0; i < 100; i++)
        {
            listener.ProcessEvent("GCStart_V2", start, startPayload);
            listener.ProcessEvent("GCEnd_V1", end, endPayload);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            listener.ProcessEvent("GCStart_V2", start, startPayload);
            listener.ProcessEvent("GCEnd_V1", end, endPayload);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task GCEventListenerEvictsOldestStaleStartAndReportsBoundedStateOverflow()
    {
        const int correlationCapacity = 64;
        var actual = new List<GCEventStatistics>(1);
        var errors = new List<Exception>(1);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCEventListener(value =>
        {
            actual.Add(value);
            completed.TrySetResult();
            return Task.CompletedTask;
        }, errors.Add);

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        try
        {
            for (var i = 0; i < correlationCapacity; i++)
            {
                listener.ProcessEvent("GCStart_V2", origin.AddTicks(i), [(uint)i, 0U, 0U, 0U]);
            }

            listener.ProcessEvent("GCStart_V2", origin.AddTicks(correlationCapacity), [(uint)correlationCapacity, 0U, 1U, 0U]);
            listener.ProcessEvent("GCEnd_V1", origin.AddTicks(correlationCapacity + 5_000), [(uint)correlationCapacity, 0U]);
            listener.ProcessEvent("GCEnd_V1", origin.AddTicks(correlationCapacity + 10_000), [0U, 0U]);

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var result = Assert.Single(actual).GCStartEndStatistics;
        Assert.Equal((uint)correlationCapacity, result.Index);
        Assert.Equal(1U, result.Reason);
        Assert.Single(errors);
        var error = Assert.IsType<InvalidOperationException>(errors[0]);
        Assert.Equal("GC correlation capacity exceeded. Evicted start with index 0 to store start with index 64.", error.Message);
    }

    [Fact]
    public async Task GCEventListenerIgnoresEndWithoutStartAndProcessesLaterPair()
    {
        var actual = new List<GCEventStatistics>(1);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCEventListener(value =>
        {
            actual.Add(value);
            completed.TrySetResult();
            return Task.CompletedTask;
        });

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        try
        {
            listener.ProcessEvent("GCEnd_V1", origin, [42U, 2U]);
            listener.ProcessEvent("GCStart_V2", origin.AddTicks(10_000), [43U, 0U, 1U, 0U]);
            listener.ProcessEvent("GCEnd_V1", origin.AddTicks(15_000), [43U, 0U]);

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        Assert.Equal(43U, Assert.Single(actual).GCStartEndStatistics.Index);
    }

    [Fact]
    public async Task GCEventListenerIgnoresRestartWithoutSuspendAndProcessesLaterPair()
    {
        var actual = new List<GCEventStatistics>(1);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCEventListener(value =>
        {
            actual.Add(value);
            completed.TrySetResult();
            return Task.CompletedTask;
        });

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        try
        {
            listener.ProcessEvent("GCRestartEEEnd_V1", origin, []);
            listener.ProcessEvent("GCSuspendEEBegin_V1", origin.AddTicks(10_000), [1U, 123U]);
            listener.ProcessEvent("GCRestartEEEnd_V1", origin.AddTicks(15_000), []);

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var result = Assert.Single(actual).GCSuspendStatistics;
        Assert.Equal(1U, result.Reason);
        Assert.Equal(123U, result.Count);
        Assert.Equal(0.5, result.DurationMillisec);
    }

    [Fact]
    public async Task ContentionEventListenerPreservesEveryEventAtChannelCapacity()
    {
        var actual = new List<ContentionEventStatistics>(ChannelCapacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        TestableContentionEventListener? listener = null;
        listener = new TestableContentionEventListener(value =>
        {
            actual.Add(value);
            if (actual.Count == ChannelCapacity)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });
        using (listener)
        {
            var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < ChannelCapacity; i++)
            {
                listener.ProcessEvent("ContentionStop_V1", origin.AddTicks(i), [(byte)(i % 2), 0U, i + 0.5]);
            }

            listener.EnableReading();
            await listener.OnReadResultAsync(cts.Token);
        }

        Assert.Equal(ChannelCapacity, actual.Count);
        for (var i = 0; i < ChannelCapacity; i++)
        {
            Assert.Equal((byte)(i % 2), actual[i].Flag);
            Assert.Equal(i + 0.5, actual[i].DurationNs);
        }
    }

    [Fact]
    public async Task ThreadPoolEventListenerPreservesEveryTrackedEventAtChannelCapacity()
    {
        var actual = new List<ThreadPoolEventStatistics>(ChannelCapacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        TestableThreadPoolEventListener? listener = null;
        listener = new TestableThreadPoolEventListener(value =>
        {
            actual.Add(value);
            if (actual.Count == ChannelCapacity)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });
        using (listener)
        {
            var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < ChannelCapacity / 2; i++)
            {
                listener.ProcessEvent("ThreadPoolWorkerThreadAdjustmentAdjustment", origin.AddTicks(i * 2L), [i + 0.25, (uint)(10 + i), (uint)(i % 3)]);
                listener.ProcessEvent("ThreadPoolWorkerThreadStop_V1", origin.AddTicks((i * 2L) + 1), [(uint)(20 + i)]);
            }

            // These events are intentionally filtered and must not alter the tracked sequence.
            listener.ProcessEvent("ThreadPoolWorkerThreadAdjustmentAdjustment", origin, [1.0, 1U, 3U]);
            listener.ProcessEvent("ThreadPoolWorkerThreadWait", origin, []);

            listener.EnableReading();
            await listener.OnReadResultAsync(cts.Token);
        }

        Assert.Equal(ChannelCapacity, actual.Count);
        for (var i = 0; i < ChannelCapacity / 2; i++)
        {
            var adjustment = actual[i * 2];
            Assert.Equal(ThreadPoolStatisticType.ThreadPoolAdjustment, adjustment.Type);
            Assert.Equal(i + 0.25, adjustment.ThreadPoolAdjustment.AverageThrouput);
            Assert.Equal((uint)(10 + i), adjustment.ThreadPoolAdjustment.NewWorkerThreads);
            Assert.Equal((uint)(i % 3), adjustment.ThreadPoolAdjustment.Reason);

            var worker = actual[(i * 2) + 1];
            Assert.Equal(ThreadPoolStatisticType.ThreadPoolWorkerStartStop, worker.Type);
            Assert.Equal((uint)(20 + i), worker.ThreadPoolWorker.ActiveWrokerThreads);
        }
    }

    private sealed class TestableGCEventListener(Func<GCEventStatistics, Task> onEventEmit, Action<Exception>? onEventError = null)
        : GCEventListener(onEventEmit, onEventError ?? (exception => throw exception))
    {
        public void EnableReading() => Enabled = true;
        public void DisableReading() => Enabled = false;
    }

    private sealed class TestableContentionEventListener(Func<ContentionEventStatistics, Task> onEventEmit)
        : ContentionEventListener(onEventEmit, exception => throw exception)
    {
        public void EnableReading() => Enabled = true;
        public void DisableReading() => Enabled = false;
    }

    private sealed class TestableThreadPoolEventListener(Func<ThreadPoolEventStatistics, Task> onEventEmit)
        : ThreadPoolEventListener(onEventEmit, exception => throw exception)
    {
        public void EnableReading() => Enabled = true;
        public void DisableReading() => Enabled = false;
    }
}
