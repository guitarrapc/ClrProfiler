using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;

namespace ClrProfiler.UnitTest;

[NotInParallel]
public class EventListenerDataIntegrityTest
{
    private const int ChannelCapacity = 50;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Test]
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

        await Assert.That(actual.Count).IsEqualTo(ChannelCapacity);
        for (var i = 0; i < ChannelCapacity / 2; i++)
        {
            var startEnd = actual[i * 2];
            await Assert.That(startEnd.Type).IsEqualTo(GCEventType.GCStartEnd);
            await Assert.That(startEnd.GCStartEndStatistics.Index).IsEqualTo((uint)i);
            await Assert.That(startEnd.GCStartEndStatistics.Type).IsEqualTo((uint)(i % 2));
            await Assert.That(startEnd.GCStartEndStatistics.Generation).IsEqualTo((uint)(i % 3));
            await Assert.That(startEnd.GCStartEndStatistics.Reason).IsEqualTo((uint)(i % 3));
            await Assert.That(startEnd.GCStartEndStatistics.DurationMillsec).IsEqualTo(0.5);

            var suspend = actual[(i * 2) + 1];
            await Assert.That(suspend.Type).IsEqualTo(GCEventType.GCSuspend);
            await Assert.That(suspend.GCSuspendStatistics.Reason).IsEqualTo(1U);
            await Assert.That(suspend.GCSuspendStatistics.Count).IsEqualTo((uint)(100 + i));
            await Assert.That(suspend.GCSuspendStatistics.DurationMillisec).IsEqualTo(0.25);
        }
    }

    [Test]
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

        await Assert.That(actual.Count).IsEqualTo(2);

        var foreground = actual[0].GCStartEndStatistics;
        await Assert.That(foreground.Index).IsEqualTo(101U);
        await Assert.That(foreground.Type).IsEqualTo(0U);
        await Assert.That(foreground.Generation).IsEqualTo(0U);
        await Assert.That(foreground.Reason).IsEqualTo(0U);
        await Assert.That(foreground.GCStartTime).IsEqualTo(origin.AddTicks(10_000).Ticks);
        await Assert.That(foreground.DurationMillsec).IsEqualTo(0.5);

        var background = actual[1].GCStartEndStatistics;
        await Assert.That(background.Index).IsEqualTo(100U);
        await Assert.That(background.Type).IsEqualTo(1U);
        await Assert.That(background.Generation).IsEqualTo(2U);
        await Assert.That(background.Reason).IsEqualTo(4U);
        await Assert.That(background.GCStartTime).IsEqualTo(origin.Ticks);
        await Assert.That(background.DurationMillsec).IsEqualTo(5.0);
    }

    [Test]
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

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        await Assert.That(actual.Select(x => x.GCStartEndStatistics.Index).SequenceEqual([64U, 0U])).IsTrue();
        await Assert.That(actual[0].GCStartEndStatistics.DurationMillsec).IsEqualTo(0.5);
        await Assert.That(actual[1].GCStartEndStatistics.DurationMillsec).IsEqualTo(5.0);
    }

    [Test]
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
                    startBarrier.SignalAndWait(TestContext.Current!.Execution.CancellationToken);
                    listener.ProcessEvent("GCStart_V2", origin, [0U, 2U, 4U, 1U]);
                }, TestContext.Current!.Execution.CancellationToken),
                Task.Run(() =>
                {
                    startBarrier.SignalAndWait(TestContext.Current!.Execution.CancellationToken);
                    listener.ProcessEvent("GCStart_V2", origin.AddTicks(10_000), [64U, 0U, 0U, 0U]);
                }, TestContext.Current!.Execution.CancellationToken));

            await Task.WhenAll(
                Task.Run(() =>
                {
                    endBarrier.SignalAndWait(TestContext.Current!.Execution.CancellationToken);
                    listener.ProcessEvent("GCEnd_V1", origin.AddTicks(50_000), [0U, 2U]);
                }, TestContext.Current!.Execution.CancellationToken),
                Task.Run(() =>
                {
                    endBarrier.SignalAndWait(TestContext.Current!.Execution.CancellationToken);
                    listener.ProcessEvent("GCEnd_V1", origin.AddTicks(15_000), [64U, 0U]);
                }, TestContext.Current!.Execution.CancellationToken));

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        await Assert.That(actual.Select(x => x.GCStartEndStatistics.Index).Order().SequenceEqual([0U, 64U])).IsTrue();
    }

    [Test]
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

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var result = (await Assert.That(actual).HasSingleItem()).GCStartEndStatistics;
        await Assert.That(result.Index).IsEqualTo(0U);
        await Assert.That(result.GCEndTime).IsEqualTo(origin.AddTicks(50_000).Ticks);
        await Assert.That(result.DurationMillsec).IsEqualTo(5.0);
        await Assert.That(errors).HasSingleItem();
    }

    [Test]
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

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var result = (await Assert.That(actual).HasSingleItem()).GCStartEndStatistics;
        await Assert.That(result.GCStartTime).IsEqualTo(origin.Ticks);
        await Assert.That(result.DurationMillsec).IsEqualTo(5.0);
        await Assert.That(errors).HasSingleItem();
    }

    [Test]
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

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var result = (await Assert.That(actual).HasSingleItem()).GCStartEndStatistics;
        await Assert.That(result.Index).IsEqualTo(1U);
        await Assert.That(result.Type).IsEqualTo(1U);
        await Assert.That(result.Generation).IsEqualTo(2U);
        await Assert.That(result.Reason).IsEqualTo(4U);
        await Assert.That(result.DurationMillsec).IsEqualTo(0.5);
    }

    [Test]
    public async Task GCEventListenerNormalStartEndHotPathDoesNotAllocate()
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

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
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

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var result = (await Assert.That(actual).HasSingleItem()).GCStartEndStatistics;
        await Assert.That(result.Index).IsEqualTo((uint)correlationCapacity);
        await Assert.That(result.Reason).IsEqualTo(1U);
        await Assert.That(errors).HasSingleItem();
        await Assert.That(errors[0]).IsTypeOf<InvalidOperationException>();
        var error = (InvalidOperationException)errors[0];
        await Assert.That(error.Message).IsEqualTo("GC correlation capacity exceeded. Evicted start with index 0 to store start with index 64.");
    }

    [Test]
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

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        await Assert.That((await Assert.That(actual).HasSingleItem()).GCStartEndStatistics.Index).IsEqualTo(43U);
    }

    [Test]
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

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var result = (await Assert.That(actual).HasSingleItem()).GCSuspendStatistics;
        await Assert.That(result.Reason).IsEqualTo(1U);
        await Assert.That(result.Count).IsEqualTo(123U);
        await Assert.That(result.DurationMillisec).IsEqualTo(0.5);
    }

    [Test]
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

        await Assert.That(actual.Count).IsEqualTo(ChannelCapacity);
        for (var i = 0; i < ChannelCapacity; i++)
        {
            await Assert.That(actual[i].Flag).IsEqualTo((byte)(i % 2));
            await Assert.That(actual[i].DurationNs).IsEqualTo(i + 0.5);
        }
    }

    [Test]
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

        await Assert.That(actual.Count).IsEqualTo(ChannelCapacity);
        for (var i = 0; i < ChannelCapacity / 2; i++)
        {
            var adjustment = actual[i * 2];
            await Assert.That(adjustment.Type).IsEqualTo(ThreadPoolStatisticType.ThreadPoolAdjustment);
            await Assert.That(adjustment.ThreadPoolAdjustment.AverageThrouput).IsEqualTo(i + 0.25);
            await Assert.That(adjustment.ThreadPoolAdjustment.NewWorkerThreads).IsEqualTo((uint)(10 + i));
            await Assert.That(adjustment.ThreadPoolAdjustment.Reason).IsEqualTo((uint)(i % 3));

            var worker = actual[(i * 2) + 1];
            await Assert.That(worker.Type).IsEqualTo(ThreadPoolStatisticType.ThreadPoolWorkerStartStop);
            await Assert.That(worker.ThreadPoolWorker.ActiveWrokerThreads).IsEqualTo((uint)(20 + i));
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
