using ClrProfiler.Statistics;
using ClrProfiler.TimerListeners;

namespace ClrProfiler.UnitTest;

[NotInParallel]
public class TimerListenerDataIntegrityTest
{
    private const int ChannelCapacity = 50;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UnusedTimerPeriod = TimeSpan.FromDays(1);

    [Test]
    public async Task GCInfoTimerListenerPreservesEverySampleAtChannelCapacity()
    {
        var actual = new List<GCInfoStatistics>(ChannelCapacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        TestableGCInfoTimerListener? listener = null;
        listener = new TestableGCInfoTimerListener(value =>
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
            for (var i = 0; i < ChannelCapacity; i++)
            {
                listener.EventCreatedHandler();
            }

            listener.EnableReading();
            await listener.OnReadResultAsync(cts.Token);
        }

        await Assert.That(actual.Count).IsEqualTo(ChannelCapacity);
        foreach (var value in actual)
        {
            await Assert.That(value.Date > DateTime.MinValue).IsTrue();
            await Assert.That(value.HeapSize >= 0).IsTrue();
            await Assert.That(value.TotalAllocationBytes >= 0).IsTrue();
            await Assert.That(value.Gen0Count >= 0).IsTrue();
            await Assert.That(value.Gen1Count >= 0).IsTrue();
            await Assert.That(value.Gen2Count >= 0).IsTrue();
        }
    }

    [Test]
    public async Task GCInfoTimerListenerSamplesPublicRuntimeGcCountersWithoutReflection()
    {
        var actual = new List<GCInfoStatistics>(1);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCInfoTimerListener(value =>
        {
            actual.Add(value);
            cts.Cancel();
            return Task.CompletedTask;
        });

        // Force a full collection so the last-GC data behind GC.GetGCMemoryInfo and the
        // cumulative pause total are both guaranteed to be populated.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var pauseBeforeSample = GC.GetTotalPauseDuration().TotalMilliseconds;

        listener.EventCreatedHandler();
        var pauseAfterSample = GC.GetTotalPauseDuration().TotalMilliseconds;

        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        var sample = await Assert.That(actual).HasSingleItem();
        await Assert.That(pauseBeforeSample > 0D).IsTrue();
        await Assert.That(sample.TotalPauseTimeMillisec >= pauseBeforeSample).IsTrue();
        await Assert.That(sample.TotalPauseTimeMillisec <= pauseAfterSample).IsTrue();
        // Generation sizes come from GC.GetGCMemoryInfo().GenerationInfo. After a forced full
        // collection the heap holds runtime objects, so the generations cannot all be empty.
        await Assert.That(sample.Gen0Size + sample.Gen1Size + sample.Gen2Size + sample.LohSize > 0UL).IsTrue();
    }

    [Test]
    public async Task GCInfoTimerListenerReportsSamplesDroppedBeyondChannelCapacity()
    {
        var actual = new List<GCInfoStatistics>(ChannelCapacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCInfoTimerListener(value =>
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
    public async Task GCInfoTimerListenerReportsMissingTimeInGcReflectionOnceAndKeepsSampling()
    {
        var actual = new List<GCInfoStatistics>(2);
        var errors = new List<Exception>(1);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCInfoTimerListenerWithoutTimeInGc(value =>
        {
            actual.Add(value);
            if (actual.Count == 2)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        }, errors.Add);

        listener.EventCreatedHandler();
        listener.EventCreatedHandler();

        listener.EnableReading();
        await listener.OnReadResultAsync(cts.Token);

        // The unavailable internal API must be reported once, not per sample and not silently,
        // and sampling must continue with time-in-GC pinned to 0.
        await Assert.That(actual).Count().IsEqualTo(2);
        await Assert.That(actual.All(value => value.TimeInGc == 0)).IsTrue();
        var error = await Assert.That(errors).HasSingleItem();
        await Assert.That(error.Message).Contains("GetLastGCPercentTimeInGC");
    }

    [Test]
    public async Task ProcessInfoTimerListenerPreservesEverySampleAtChannelCapacity()
    {
        var actual = new List<ProcessInfoStatistics>(ChannelCapacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        TestableProcessInfoTimerListener? listener = null;
        listener = new TestableProcessInfoTimerListener(value =>
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
            for (var i = 0; i < ChannelCapacity; i++)
            {
                listener.EventCreatedHandler();
            }

            listener.EnableReading();
            await listener.OnReadResultAsync(cts.Token);
        }

        await Assert.That(actual.Count).IsEqualTo(ChannelCapacity);
        foreach (var value in actual)
        {
            await Assert.That(value.Date > DateTime.MinValue).IsTrue();
            await Assert.That(value.Cpu >= 0).IsTrue();
            await Assert.That(value.WorkingSet > 0).IsTrue();
            await Assert.That(value.PrivateBytes > 0).IsTrue();
        }
    }

    [Test]
    public async Task ProcessInfoTimerListenerReportsSamplesDroppedBeyondChannelCapacity()
    {
        var actual = new List<ProcessInfoStatistics>(ChannelCapacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableProcessInfoTimerListener(value =>
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
    public async Task ThreadInfoTimerListenerPreservesEverySampleAtChannelCapacity()
    {
        var actual = new List<ThreadInfoStatistics>(ChannelCapacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        TestableThreadInfoTimerListener? listener = null;
        listener = new TestableThreadInfoTimerListener(value =>
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
            for (var i = 0; i < ChannelCapacity; i++)
            {
                listener.EventCreatedHandler();
            }

            listener.EnableReading();
            await listener.OnReadResultAsync(cts.Token);
        }

        await Assert.That(actual.Count).IsEqualTo(ChannelCapacity);
        foreach (var value in actual)
        {
            await Assert.That(value.Date > DateTime.MinValue).IsTrue();
            await Assert.That(value.AvailableWorkerThreads >= 0).IsTrue();
            await Assert.That(value.AvailableCompletionPortThreads >= 0).IsTrue();
            await Assert.That(value.MaxWorkerThreads >= value.AvailableWorkerThreads).IsTrue();
            await Assert.That(value.MaxCompletionPortThreads >= value.AvailableCompletionPortThreads).IsTrue();
            await Assert.That(value.UsingWorkerThreads).IsEqualTo(value.MaxWorkerThreads - value.AvailableWorkerThreads);
            await Assert.That(value.UsingCompletionPortThreads).IsEqualTo(value.MaxCompletionPortThreads - value.AvailableCompletionPortThreads);
        }
    }

    [Test]
    public async Task ThreadInfoTimerListenerReportsSamplesDroppedBeyondChannelCapacity()
    {
        var actual = new List<ThreadInfoStatistics>(ChannelCapacity);
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableThreadInfoTimerListener(value =>
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

    private sealed class TestableGCInfoTimerListener(Func<GCInfoStatistics, Task> onEventEmit)
        : GCInfoTimerListener(onEventEmit, exception => throw exception, UnusedTimerPeriod, UnusedTimerPeriod)
    {
        public void EnableReading() => Enabled = true;
        public void DisableReading() => Enabled = false;
    }

    private sealed class TestableGCInfoTimerListenerWithoutTimeInGc(Func<GCInfoStatistics, Task> onEventEmit, Action<Exception> onEventError)
        : GCInfoTimerListener(onEventEmit, onEventError, UnusedTimerPeriod, UnusedTimerPeriod, getLastGCPercentTimeInGC: null)
    {
        public void EnableReading() => Enabled = true;
    }

    private sealed class TestableProcessInfoTimerListener(Func<ProcessInfoStatistics, Task> onEventEmit)
        : ProcessInfoTimerListener(onEventEmit, exception => throw exception, UnusedTimerPeriod, UnusedTimerPeriod)
    {
        public void EnableReading() => Enabled = true;
        public void DisableReading() => Enabled = false;
    }

    private sealed class TestableThreadInfoTimerListener(Func<ThreadInfoStatistics, Task> onEventEmit)
        : ThreadInfoTimerListener(onEventEmit, exception => throw exception, UnusedTimerPeriod, UnusedTimerPeriod)
    {
        public void EnableReading() => Enabled = true;
        public void DisableReading() => Enabled = false;
    }
}
