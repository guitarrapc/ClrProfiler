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

    private sealed class TestableGCInfoTimerListener(Func<GCInfoStatistics, Task> onEventEmit)
        : GCInfoTimerListener(onEventEmit, exception => throw exception, UnusedTimerPeriod, UnusedTimerPeriod)
    {
        public void EnableReading() => Enabled = true;
        public void DisableReading() => Enabled = false;
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
