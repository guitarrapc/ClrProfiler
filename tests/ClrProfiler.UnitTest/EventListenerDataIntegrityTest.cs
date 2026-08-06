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

    private sealed class TestableGCEventListener(Func<GCEventStatistics, Task> onEventEmit)
        : GCEventListener(onEventEmit, exception => throw exception)
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
