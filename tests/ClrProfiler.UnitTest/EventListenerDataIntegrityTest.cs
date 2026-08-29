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
        var capacity = GCEventListener.ChannelCapacity;

        // The GC channel is intentionally larger than the other listeners' channels: a gen0
        // burst emits start/end, suspend, and heap-stats values per collection while the reader
        // awaits the user callback, and each retained slot is a compact struct.
        await Assert.That(capacity).IsGreaterThan(ChannelCapacity);
        var actual = new List<GCEventStatistics>(capacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        TestableGCEventListener? listener = null;
        listener = new TestableGCEventListener(value =>
        {
            actual.Add(value);
            if (actual.Count == capacity)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });
        using (listener)
        {
            var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < capacity / 2; i++)
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

        await Assert.That(actual.Count).IsEqualTo(capacity);
        for (var i = 0; i < capacity / 2; i++)
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
    public async Task GCEventListenerReportsEventsDroppedBeyondChannelCapacity()
    {
        var capacity = GCEventListener.ChannelCapacity;
        var actual = new List<GCEventStatistics>(capacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCEventListener(value =>
        {
            actual.Add(value);
            if (actual.Count == capacity)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < capacity; i++)
        {
            listener.ProcessEvent("GCSuspendEEBegin_V1", origin.AddTicks(i * 2L), [1U, (uint)i]);
            listener.ProcessEvent("GCRestartEEEnd_V1", origin.AddTicks((i * 2L) + 1), []);
        }
        await Assert.That(listener.DroppedEventCount).IsEqualTo(0L);

        listener.ProcessEvent("GCSuspendEEBegin_V1", origin.AddTicks(capacity * 2L), [1U, (uint)capacity]);
        listener.ProcessEvent("GCRestartEEEnd_V1", origin.AddTicks((capacity * 2L) + 1), []);

        await Assert.That(listener.DroppedEventCount).IsEqualTo(1L);
        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        await Assert.That(actual).Count().IsEqualTo(capacity);
        await Assert.That(actual[0].GCSuspendStatistics.Count).IsEqualTo(1U);
        await Assert.That(actual[^1].GCSuspendStatistics.Count).IsEqualTo((uint)capacity);
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

        // Warm up past the channel capacity so the bounded channel's one-time segment growth
        // happens before the measured loop and only the steady drop-oldest path is measured.
        for (var i = 0; i < GCEventListener.ChannelCapacity + 100; i++)
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
    public async Task GCEventListenerParsesHeapStatsV2IntoHeapStatistics()
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
            // GCHeapStats_V2 payload: GenerationSize0, TotalPromotedSize0, GenerationSize1,
            // TotalPromotedSize1, GenerationSize2, TotalPromotedSize2, GenerationSize3,
            // TotalPromotedSize3, FinalizationPromotedSize, FinalizationPromotedCount,
            // PinnedObjectCount, SinkBlockCount, GCHandleCount, ClrInstanceID,
            // GenerationSize4, TotalPromotedSize4.
            listener.ProcessEvent("GCHeapStats_V2", origin,
                [100UL, 10UL, 200UL, 20UL, 300UL, 30UL, 400UL, 40UL, 55UL, 5UL, 7U, 3U, 900U, (ushort)1, 500UL, 50UL]);

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var result = await Assert.That(actual).HasSingleItem();
        await Assert.That(result.Type).IsEqualTo(GCEventType.GCHeapStats);
        var heapStats = result.GCHeapStatistics;
        await Assert.That(heapStats.Time).IsEqualTo(origin.Ticks);
        await Assert.That(heapStats.Gen0Size).IsEqualTo(100UL);
        await Assert.That(heapStats.Gen1Size).IsEqualTo(200UL);
        await Assert.That(heapStats.Gen2Size).IsEqualTo(300UL);
        await Assert.That(heapStats.LohSize).IsEqualTo(400UL);
        await Assert.That(heapStats.PohSize).IsEqualTo(500UL);
        await Assert.That(heapStats.FinalizationPromotedSize).IsEqualTo(55UL);
        await Assert.That(heapStats.PinnedObjectCount).IsEqualTo(7U);
        await Assert.That(heapStats.GCHandleCount).IsEqualTo(900U);
    }

    [Test]
    public async Task GCEventListenerParsesHeapStatsV1WithoutPohAsZero()
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
            listener.ProcessEvent("GCHeapStats_V1", origin,
                [100UL, 10UL, 200UL, 20UL, 300UL, 30UL, 400UL, 40UL, 55UL, 5UL, 7U, 3U, 900U, (ushort)1]);

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var heapStats = (await Assert.That(actual).HasSingleItem()).GCHeapStatistics;
        await Assert.That(heapStats.LohSize).IsEqualTo(400UL);
        await Assert.That(heapStats.PohSize).IsEqualTo(0UL);
        await Assert.That(heapStats.GCHandleCount).IsEqualTo(900U);
    }

    [Test]
    public async Task GCEventListenerMalformedHeapStatsReportsErrorAndProcessesLaterEvent()
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
            listener.ProcessEvent("GCHeapStats_V2", origin, [100UL]);
            listener.ProcessEvent("GCHeapStats_V2", origin.AddTicks(1),
                [100UL, 10UL, 200UL, 20UL, 300UL, 30UL, 400UL, 40UL, 55UL, 5UL, 7U, 3U, 900U, (ushort)1, 500UL, 50UL]);

            await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        await Assert.That(errors).HasSingleItem();
        var heapStats = (await Assert.That(actual).HasSingleItem()).GCHeapStatistics;
        await Assert.That(heapStats.Time).IsEqualTo(origin.AddTicks(1).Ticks);
        await Assert.That(heapStats.PohSize).IsEqualTo(500UL);
    }

    [Test]
    public async Task GCEventListenerHeapStatsHotPathDoesNotAllocate()
    {
        using var listener = new TestableGCEventListener(_ => Task.CompletedTask);
        object?[] payload = [100UL, 10UL, 200UL, 20UL, 300UL, 30UL, 400UL, 40UL, 55UL, 5UL, 7U, 3U, 900U, (ushort)1, 500UL, 50UL];
        var timeStamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Warm up past the channel capacity so the bounded channel's one-time segment growth
        // happens before the measured loop and only the steady drop-oldest path is measured.
        for (var i = 0; i < GCEventListener.ChannelCapacity + 100; i++)
        {
            listener.ProcessEvent("GCHeapStats_V2", timeStamp, payload);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            listener.ProcessEvent("GCHeapStats_V2", timeStamp, payload);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task ContentionEventListenerAtomicallyAggregatesBurstWithoutLoss()
    {
        const int eventCount = ChannelCapacity * 4;
        var actual = new List<ContentionEventStatistics>(2);
        using var cts = new CancellationTokenSource(TestTimeout);
        var observedCount = 0L;
        using var listener = new TestableContentionEventListener(value =>
        {
            actual.Add(value);
            observedCount += value.Count;
            if (observedCount == eventCount)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < eventCount; i++)
        {
            listener.ProcessEvent("ContentionStop_V1", origin.AddTicks(i), [(byte)(i % 2), 0U, i + 0.5]);
        }

        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        await Assert.That(actual).Count().IsEqualTo(2);
        var managed = actual.Single(value => value.Flag == 0);
        var native = actual.Single(value => value.Flag == 1);
        await Assert.That(managed.Count).IsEqualTo(eventCount / 2);
        await Assert.That(native.Count).IsEqualTo(eventCount / 2);
        // Every duration in the burst survives in the sum, and the largest one survives in the max.
        await Assert.That(managed.DurationNsSum).IsEqualTo(ExpectedDurationSum(eventCount, 0));
        await Assert.That(native.DurationNsSum).IsEqualTo(ExpectedDurationSum(eventCount, 1));
        await Assert.That(managed.DurationNsMax).IsEqualTo(eventCount - 2 + 0.5);
        await Assert.That(native.DurationNsMax).IsEqualTo(eventCount - 1 + 0.5);

        static double ExpectedDurationSum(int eventCount, int flag)
        {
            var sum = 0D;
            for (var i = flag; i < eventCount; i += 2)
            {
                sum += i + 0.5;
            }
            return sum;
        }
    }

    [Test]
    public async Task ContentionEventListenerAggregatesEveryDurationIntoSumAndMax()
    {
        double[] durations = [4.5, 100.25, 0.5, 12D, 100.25];
        var actual = new List<ContentionEventStatistics>(1);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableContentionEventListener(value =>
        {
            actual.Add(value);
            cts.Cancel();
            return Task.CompletedTask;
        });

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < durations.Length; i++)
        {
            listener.ProcessEvent("ContentionStop_V1", origin.AddTicks(i), [(byte)0, 0U, durations[i]]);
        }

        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        var result = await Assert.That(actual).HasSingleItem();
        await Assert.That(result.Count).IsEqualTo(5L);
        await Assert.That(result.DurationNsSum).IsEqualTo(217.5D);
        await Assert.That(result.DurationNsMax).IsEqualTo(100.25D);
        await Assert.That(result.DurationNsMean).IsEqualTo(43.5D);
        await Assert.That(result.Time).IsEqualTo(origin.AddTicks(durations.Length - 1).Ticks);
    }

    [Test]
    public async Task ContentionEventListenerResetsDurationAggregatesBetweenFlushes()
    {
        var actual = new List<ContentionEventStatistics>(2);
        using var firstFlush = new CancellationTokenSource(TestTimeout);
        using var secondFlush = new CancellationTokenSource(TestTimeout);
        var flushed = 0;
        using var listener = new TestableContentionEventListener(value =>
        {
            actual.Add(value);
            if (++flushed == 1)
            {
                firstFlush.Cancel();
            }
            else
            {
                secondFlush.Cancel();
            }
            return Task.CompletedTask;
        });
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        listener.EnableReading();

        listener.ProcessEvent("ContentionStop_V1", origin, [(byte)0, 0U, 90D]);
        listener.ProcessEvent("ContentionStop_V1", origin.AddTicks(1), [(byte)0, 0U, 10D]);
        await listener.OnReadResultAsync(firstFlush.Token);

        listener.ProcessEvent("ContentionStop_V1", origin.AddTicks(2), [(byte)0, 0U, 3D]);
        await listener.OnReadResultAsync(secondFlush.Token);

        await Assert.That(actual).Count().IsEqualTo(2);
        await Assert.That(actual[0].Count).IsEqualTo(2L);
        await Assert.That(actual[0].DurationNsSum).IsEqualTo(100D);
        await Assert.That(actual[0].DurationNsMax).IsEqualTo(90D);
        // The second flush must not carry any part of the first one's durations.
        await Assert.That(actual[1].Count).IsEqualTo(1L);
        await Assert.That(actual[1].DurationNsSum).IsEqualTo(3D);
        await Assert.That(actual[1].DurationNsMax).IsEqualTo(3D);
    }

    [Test]
    public async Task ContentionEventListenerTreatsNonFiniteDurationAsZeroWithoutCorruptingTheSum()
    {
        var actual = new List<ContentionEventStatistics>(1);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableContentionEventListener(value =>
        {
            actual.Add(value);
            cts.Cancel();
            return Task.CompletedTask;
        });
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        listener.ProcessEvent("ContentionStop_V1", origin, [(byte)0, 0U, double.NaN]);
        listener.ProcessEvent("ContentionStop_V1", origin.AddTicks(1), [(byte)0, 0U, double.PositiveInfinity]);
        listener.ProcessEvent("ContentionStop_V1", origin.AddTicks(2), [(byte)0, 0U, -5D]);
        listener.ProcessEvent("ContentionStop_V1", origin.AddTicks(3), [(byte)0, 0U, 7D]);

        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        var result = await Assert.That(actual).HasSingleItem();
        await Assert.That(result.Count).IsEqualTo(4L);
        await Assert.That(result.DurationNsSum).IsEqualTo(7D);
        await Assert.That(result.DurationNsMax).IsEqualTo(7D);
    }

    [Test]
    public async Task ContentionEventListenerKeepsOrReportsEveryConcurrentProducerSample()
    {
        const int eventCount = 10_000;
        using var cts = new CancellationTokenSource(TestTimeout);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = new List<ContentionEventStatistics>(2);
        var observedCount = 0L;
        var expectedCount = long.MaxValue;
        using var listener = new TestableContentionEventListener(value =>
        {
            actual.Add(value);
            observedCount += value.Count;
            if (observedCount == Volatile.Read(ref expectedCount))
            {
                completed.TrySetResult();
            }
            return Task.CompletedTask;
        });
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        object?[] managedPayload = [(byte)0, 0U, 2D];
        object?[] nativePayload = [(byte)1, 0U, 3D];

        Parallel.For(0, eventCount, i =>
            listener.ProcessEvent("ContentionStop_V1", origin.AddTicks(i), i % 2 == 0 ? managedPayload : nativePayload));

        Volatile.Write(ref expectedCount, eventCount - listener.DroppedEventCount);
        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        await cts.CancelAsync();
        await readerTask;

        await Assert.That(actual.Sum(value => value.Count) + listener.DroppedEventCount).IsEqualTo(eventCount);
        await Assert.That(actual.Where(value => value.Flag == 0).Sum(value => value.DurationNsSum))
            .IsEqualTo(actual.Where(value => value.Flag == 0).Sum(value => value.Count) * 2D);
        await Assert.That(actual.Where(value => value.Flag == 0).All(value => value.DurationNsMax == 2D)).IsTrue();
        await Assert.That(actual.Where(value => value.Flag == 1).Sum(value => value.DurationNsSum))
            .IsEqualTo(actual.Where(value => value.Flag == 1).Sum(value => value.Count) * 3D);
        await Assert.That(actual.Where(value => value.Flag == 1).All(value => value.DurationNsMax == 3D)).IsTrue();
    }

    [Test]
    public async Task ContentionEventListenerKeepsWindowsConsistentWhileReaderDrains()
    {
        const int eventCount = 10_000;
        const double durationNs = 2D;
        using var cts = new CancellationTokenSource(TestTimeout);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Only the single reader loop invokes the callback, and the reader task is awaited
        // before these are read, so plain accumulation is safe here.
        var observedCount = 0L;
        var observedDurationSum = 0D;
        var observedDurationMax = 0D;
        var expectedCount = long.MaxValue;
        using var listener = new TestableContentionEventListener(value =>
        {
            observedCount += value.Count;
            observedDurationSum += value.DurationNsSum;
            observedDurationMax = Math.Max(observedDurationMax, value.DurationNsMax);
            if (observedCount == Volatile.Read(ref expectedCount))
            {
                completed.TrySetResult();
            }
            return Task.CompletedTask;
        });
        object?[] payload = [(byte)0, 0U, durationNs];

        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        Parallel.For(0, eventCount, i =>
            listener.ProcessEvent("ContentionStop_V1", DateTime.UnixEpoch.AddTicks(i), payload));
        Volatile.Write(ref expectedCount, eventCount - listener.DroppedEventCount);
        if (Volatile.Read(ref observedCount) == expectedCount)
        {
            completed.TrySetResult();
        }
        await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        await cts.CancelAsync();
        await readerTask;

        await Assert.That(observedCount + listener.DroppedEventCount).IsEqualTo(eventCount);
        await Assert.That(observedDurationSum).IsEqualTo(observedCount * durationNs);
        await Assert.That(observedDurationMax).IsEqualTo(durationNs);
    }

    [Test]
    public async Task ContentionEventListenerKeepsCountAndDurationInTheSameFlushWindow()
    {
        const int eventCount = 10_000;
        const double durationNs = 2D;
        using var cts = new CancellationTokenSource(TestTimeout);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCount = 0L;
        var inconsistentWindow = false;
        var expectedCount = long.MaxValue;
        using var listener = new TestableContentionEventListener(value =>
        {
            observedCount += value.Count;
            inconsistentWindow |= value.DurationNsSum != value.Count * durationNs;
            if (observedCount == Volatile.Read(ref expectedCount))
            {
                completed.TrySetResult();
            }
            return Task.CompletedTask;
        });
        object?[] payload = [(byte)0, 0U, durationNs];

        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        Parallel.For(0, eventCount, i =>
            listener.ProcessEvent("ContentionStop_V1", DateTime.UnixEpoch.AddTicks(i), payload));
        Volatile.Write(ref expectedCount, eventCount - listener.DroppedEventCount);
        if (Volatile.Read(ref observedCount) == expectedCount)
        {
            completed.TrySetResult();
        }
        await completed.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        await cts.CancelAsync();
        await readerTask;

        await Assert.That(observedCount + listener.DroppedEventCount).IsEqualTo(eventCount);
        await Assert.That(inconsistentWindow).IsFalse();
    }

    [Test]
    public async Task ContentionEventListenerReportsSamplesDroppedAtTheBoundedQueue()
    {
        var observedCount = 0L;
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableContentionEventListener(value =>
        {
            observedCount += value.Count;
            if (observedCount == ContentionEventListener.EventQueueCapacity)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });
        object?[] payload = [(byte)0, 0U, 1D];

        for (var i = 0; i <= ContentionEventListener.EventQueueCapacity; i++)
        {
            listener.ProcessEvent("ContentionStop_V1", DateTime.UnixEpoch.AddTicks(i), payload);
        }

        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        await Assert.That(observedCount).IsEqualTo(ContentionEventListener.EventQueueCapacity);
        await Assert.That(listener.DroppedEventCount).IsEqualTo(1L);
    }

    [Test]
    public async Task ContentionEventListenerDoesNotMergeAggregatesAcrossStopRestart()
    {
        var actual = new List<ContentionEventStatistics>(2);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableContentionEventListener(value =>
        {
            actual.Add(value);
            if (actual.Count == 2)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });
        var beforeStop = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var afterRestart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // A burst arrives while nothing is reading, then the listener stops.
        for (var i = 0; i < 100; i++)
        {
            listener.ProcessEvent("ContentionStop_V1", beforeStop, [(byte)0, 0U, 1D]);
        }
        listener.Stop();

        // Much later the listener restarts and a single new event arrives.
        listener.Restart();
        listener.ProcessEvent("ContentionStop_V1", afterRestart, [(byte)0, 0U, 9D]);

        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        // Two separate values: what was pending at Stop must not be folded into the events that
        // arrive after Restart, and must keep its own timestamp.
        await Assert.That(actual).Count().IsEqualTo(2);
        await Assert.That(actual[0].Count).IsEqualTo(100L);
        await Assert.That(actual[0].DurationNsSum).IsEqualTo(100D);
        await Assert.That(actual[0].DurationNsMax).IsEqualTo(1D);
        await Assert.That(actual[0].Time).IsEqualTo(beforeStop.Ticks);
        await Assert.That(actual[1].Count).IsEqualTo(1L);
        await Assert.That(actual[1].DurationNsSum).IsEqualTo(9D);
        await Assert.That(actual[1].DurationNsMax).IsEqualTo(9D);
        await Assert.That(actual[1].Time).IsEqualTo(afterRestart.Ticks);
    }

    [Test]
    public async Task ContentionEventListenerStopWithNothingPendingEmitsNothing()
    {
        var actual = new List<ContentionEventStatistics>(1);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        using var listener = new TestableContentionEventListener(value =>
        {
            actual.Add(value);
            return Task.CompletedTask;
        });

        listener.Stop();
        listener.Stop();
        listener.Restart();

        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        await Assert.That(actual).IsEmpty();
    }

    [Test]
    public async Task ContentionEventListenerStopDeliversPendingAggregateToALaterReader()
    {
        var actual = new List<ContentionEventStatistics>(2);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableContentionEventListener(value =>
        {
            actual.Add(value);
            if (actual.Sum(x => x.Count) == 2L)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        listener.ProcessEvent("ContentionStop_V1", origin, [(byte)0, 0U, 4D]);
        listener.ProcessEvent("ContentionStop_V1", origin.AddTicks(1), [(byte)1, 0U, 6D]);
        listener.Stop();

        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        // Stopping must not discard what was already observed.
        await Assert.That(actual.Sum(value => value.Count)).IsEqualTo(2L);
        await Assert.That(actual.Sum(value => value.DurationNsSum)).IsEqualTo(10D);
    }

    [Test]
    public async Task ContentionEventListenerStopPreservesEveryPendingFlag()
    {
        const int flagCount = byte.MaxValue + 1;
        var actual = new List<ContentionEventStatistics>(flagCount);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableContentionEventListener(value =>
        {
            actual.Add(value);
            if (actual.Count == flagCount)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var flag = 0; flag < flagCount; flag++)
        {
            listener.ProcessEvent("ContentionStop_V1", origin.AddTicks(flag), [(byte)flag, 0U, flag + 0.5D]);
        }
        listener.Stop();

        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        await Assert.That(actual).Count().IsEqualTo(flagCount);
        await Assert.That(actual.Sum(value => value.Count)).IsEqualTo(flagCount);
        await Assert.That(actual.Select(value => value.Flag).Distinct()).Count().IsEqualTo(flagCount);
    }

    [Test]
    public async Task ContentionEventStatisticsEqualityIncludesEveryAggregateField()
    {
        var baseline = new ContentionEventStatistics(1, 0, 3, 90D, 40D);

        await Assert.That(baseline.Equals(new ContentionEventStatistics(1, 0, 3, 90D, 40D))).IsTrue();
        await Assert.That(baseline == new ContentionEventStatistics(1, 0, 3, 90D, 40D)).IsTrue();
        await Assert.That(baseline != new ContentionEventStatistics(2, 0, 3, 90D, 40D)).IsTrue();
        await Assert.That(baseline != new ContentionEventStatistics(1, 1, 3, 90D, 40D)).IsTrue();
        await Assert.That(baseline != new ContentionEventStatistics(1, 0, 4, 90D, 40D)).IsTrue();
        await Assert.That(baseline != new ContentionEventStatistics(1, 0, 3, 91D, 40D)).IsTrue();
        await Assert.That(baseline != new ContentionEventStatistics(1, 0, 3, 90D, 41D)).IsTrue();
    }

    [Test]
    public async Task ContentionEventStatisticsSingleEventConstructorIsItsOwnSumAndMax()
    {
        var single = new ContentionEventStatistics(1, 0, 12.5D);

        await Assert.That(single.Count).IsEqualTo(1L);
        await Assert.That(single.DurationNsSum).IsEqualTo(12.5D);
        await Assert.That(single.DurationNsMax).IsEqualTo(12.5D);
        await Assert.That(single.DurationNsMean).IsEqualTo(12.5D);
    }

    [Test]
    public async Task ContentionEventStatisticsMeanIsZeroWhenNothingWasAggregated()
    {
        var empty = new ContentionEventStatistics(1, 0, 0, 0D, 0D);

        await Assert.That(empty.DurationNsMean).IsEqualTo(0D);
    }

    [Test]
    public async Task ContentionEventListenerSupportedPayloadHotPathsDoNotAllocate()
    {
        using var listener = new TestableContentionEventListener(_ => Task.CompletedTask);
        object?[] typedPayload = [(byte)1, 0U, 123.5D];
        object?[] stringPayload = ["1", 0U, "123.5"];
        var timeStamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 100; i++)
        {
            listener.ProcessEvent("ContentionStop_V1", timeStamp, typedPayload);
            listener.ProcessEvent("ContentionStop_V1", timeStamp, stringPayload);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            listener.ProcessEvent("ContentionStop_V1", timeStamp, typedPayload);
            listener.ProcessEvent("ContentionStop_V1", timeStamp, stringPayload);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task ContentionEventListenerAcceptsNumericStringPayload()
    {
        var actual = new List<ContentionEventStatistics>(1);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableContentionEventListener(value =>
        {
            actual.Add(value);
            cts.Cancel();
            return Task.CompletedTask;
        });

        listener.ProcessEvent("ContentionStop_V1", DateTime.UnixEpoch, ["1", 0U, "123.5"]);
        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        var result = await Assert.That(actual).HasSingleItem();
        await Assert.That(result.Flag).IsEqualTo((byte)1);
        await Assert.That(result.DurationNsSum).IsEqualTo(123.5D);
        await Assert.That(result.DurationNsMax).IsEqualTo(123.5D);
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

    [Test]
    public async Task ThreadPoolEventListenerPreservesCapacityBoundedEventsFromConcurrentWriters()
    {
        var writerCount = 5;
        await Assert.That(ChannelCapacity % writerCount).IsEqualTo(0);
        var eventsPerWriter = ChannelCapacity / writerCount;
        var actual = new List<ThreadPoolEventStatistics>(ChannelCapacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var start = new ManualResetEventSlim();
        using var listener = new TestableThreadPoolEventListener(value =>
        {
            actual.Add(value);
            if (actual.Count == ChannelCapacity)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });
        var writers = new Thread[writerCount];

        for (var writerIndex = 0; writerIndex < writerCount; writerIndex++)
        {
            var capturedWriterIndex = writerIndex;
            writers[writerIndex] = new Thread(() =>
            {
                start.Wait();
                for (var eventIndex = 0; eventIndex < eventsPerWriter; eventIndex++)
                {
                    var value = (uint)((capturedWriterIndex * eventsPerWriter) + eventIndex + 1);
                    listener.ProcessEvent("ThreadPoolWorkerThreadStop_V1", DateTime.UnixEpoch.AddTicks(value), [value]);
                }
            });
            writers[writerIndex].Start();
        }

        start.Set();
        foreach (var writer in writers)
        {
            if (!writer.Join(TestTimeout))
            {
                throw new TimeoutException("A concurrent ThreadPool event writer did not complete.");
            }
        }

        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        await Assert.That(actual).Count().IsEqualTo(ChannelCapacity);
        var activeWorkerCounts = actual
            .Select(value => value.ThreadPoolWorker.ActiveWrokerThreads)
            .Order()
            .ToArray();
        await Assert.That(activeWorkerCounts).IsEquivalentTo(Enumerable.Range(1, ChannelCapacity).Select(value => (uint)value));
    }

    [Test]
    public async Task ThreadPoolEventListenerReportsEventsDroppedBeyondChannelCapacity()
    {
        var actual = new List<ThreadPoolEventStatistics>(ChannelCapacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableThreadPoolEventListener(value =>
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
            listener.ProcessEvent("ThreadPoolWorkerThreadStop_V1", DateTime.UnixEpoch.AddTicks(i), [(uint)i]);
        }
        await Assert.That(listener.DroppedEventCount).IsEqualTo(0L);

        listener.ProcessEvent("ThreadPoolWorkerThreadStop_V1", DateTime.UnixEpoch.AddTicks(ChannelCapacity), [(uint)ChannelCapacity]);

        await Assert.That(listener.DroppedEventCount).IsEqualTo(1L);
        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        await Assert.That(actual).Count().IsEqualTo(ChannelCapacity);
        await Assert.That(actual[0].ThreadPoolWorker.ActiveWrokerThreads).IsEqualTo(1U);
        await Assert.That(actual[^1].ThreadPoolWorker.ActiveWrokerThreads).IsEqualTo((uint)ChannelCapacity);
    }

    [Test]
    public async Task ThreadPoolEventListenerSupportedPayloadHotPathsDoNotAllocate()
    {
        using var listener = new TestableThreadPoolEventListener(_ => Task.CompletedTask);
        object?[] typedAdjustmentPayload = [123.5D, 16U, 6U];
        object?[] typedStopPayload = [15U];
        object?[] stringAdjustmentPayload = ["123.5", "16", "6"];
        object?[] stringStopPayload = ["15"];
        var timeStamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 100; i++)
        {
            listener.ProcessEvent("ThreadPoolWorkerThreadAdjustmentAdjustment", timeStamp, typedAdjustmentPayload);
            listener.ProcessEvent("ThreadPoolWorkerThreadStop_V1", timeStamp, typedStopPayload);
            listener.ProcessEvent("ThreadPoolWorkerThreadAdjustmentAdjustment", timeStamp, stringAdjustmentPayload);
            listener.ProcessEvent("ThreadPoolWorkerThreadStop_V1", timeStamp, stringStopPayload);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            listener.ProcessEvent("ThreadPoolWorkerThreadAdjustmentAdjustment", timeStamp, typedAdjustmentPayload);
            listener.ProcessEvent("ThreadPoolWorkerThreadStop_V1", timeStamp, typedStopPayload);
            listener.ProcessEvent("ThreadPoolWorkerThreadAdjustmentAdjustment", timeStamp, stringAdjustmentPayload);
            listener.ProcessEvent("ThreadPoolWorkerThreadStop_V1", timeStamp, stringStopPayload);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task ThreadPoolEventListenerAcceptsNumericStringPayload()
    {
        var actual = new List<ThreadPoolEventStatistics>(2);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableThreadPoolEventListener(value =>
        {
            actual.Add(value);
            if (actual.Count == 2)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        });

        listener.ProcessEvent("ThreadPoolWorkerThreadAdjustmentAdjustment", DateTime.UnixEpoch, ["123.5", "16", "6"]);
        listener.ProcessEvent("ThreadPoolWorkerThreadStop_V1", DateTime.UnixEpoch, ["15"]);
        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        await Assert.That(actual).Count().IsEqualTo(2);
        await Assert.That(actual[0].ThreadPoolAdjustment.AverageThrouput).IsEqualTo(123.5D);
        await Assert.That(actual[0].ThreadPoolAdjustment.NewWorkerThreads).IsEqualTo(16U);
        await Assert.That(actual[0].ThreadPoolAdjustment.Reason).IsEqualTo(6U);
        await Assert.That(actual[1].ThreadPoolWorker.ActiveWrokerThreads).IsEqualTo(15U);
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
