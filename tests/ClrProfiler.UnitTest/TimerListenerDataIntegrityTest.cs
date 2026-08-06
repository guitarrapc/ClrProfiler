using ClrProfiler.Statistics;
using ClrProfiler.TimerListeners;

namespace ClrProfiler.UnitTest;

[Collection(nameof(TestCollectionDefinition))]
public class TimerListenerDataIntegrityTest
{
    private const int ChannelCapacity = 50;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UnusedTimerPeriod = TimeSpan.FromDays(1);

    [Fact]
    public async Task GCInfoTimerListenerPreservesEverySampleAtChannelCapacity()
    {
        var actual = new List<GCInfoStatistics>(ChannelCapacity);
        TestableGCInfoTimerListener? listener = null;
        listener = new TestableGCInfoTimerListener(value =>
        {
            actual.Add(value);
            if (actual.Count == ChannelCapacity)
            {
                listener!.DisableReading();
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
            using var cts = new CancellationTokenSource(TestTimeout);
            await listener.OnReadResultAsync(cts.Token);
        }

        Assert.Equal(ChannelCapacity, actual.Count);
        Assert.All(actual, value =>
        {
            Assert.True(value.Date > DateTime.MinValue);
            Assert.True(value.HeapSize >= 0);
            Assert.True(value.TotalAllocationBytes >= 0);
            Assert.True(value.Gen0Count >= 0);
            Assert.True(value.Gen1Count >= 0);
            Assert.True(value.Gen2Count >= 0);
        });
    }

    [Fact]
    public async Task ProcessInfoTimerListenerPreservesEverySampleAtChannelCapacity()
    {
        var actual = new List<ProcessInfoStatistics>(ChannelCapacity);
        TestableProcessInfoTimerListener? listener = null;
        listener = new TestableProcessInfoTimerListener(value =>
        {
            actual.Add(value);
            if (actual.Count == ChannelCapacity)
            {
                listener!.DisableReading();
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
            using var cts = new CancellationTokenSource(TestTimeout);
            await listener.OnReadResultAsync(cts.Token);
        }

        Assert.Equal(ChannelCapacity, actual.Count);
        Assert.All(actual, value =>
        {
            Assert.True(value.Date > DateTime.MinValue);
            Assert.True(value.Cpu >= 0);
            Assert.True(value.WorkingSet > 0);
            Assert.True(value.PrivateBytes > 0);
        });
    }

    [Fact]
    public async Task ThreadInfoTimerListenerPreservesEverySampleAtChannelCapacity()
    {
        var actual = new List<ThreadInfoStatistics>(ChannelCapacity);
        TestableThreadInfoTimerListener? listener = null;
        listener = new TestableThreadInfoTimerListener(value =>
        {
            actual.Add(value);
            if (actual.Count == ChannelCapacity)
            {
                listener!.DisableReading();
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
            using var cts = new CancellationTokenSource(TestTimeout);
            await listener.OnReadResultAsync(cts.Token);
        }

        Assert.Equal(ChannelCapacity, actual.Count);
        Assert.All(actual, value =>
        {
            Assert.True(value.Date > DateTime.MinValue);
            Assert.True(value.AvailableWorkerThreads >= 0);
            Assert.True(value.AvailableCompletionPortThreads >= 0);
            Assert.True(value.MaxWorkerThreads >= value.AvailableWorkerThreads);
            Assert.True(value.MaxCompletionPortThreads >= value.AvailableCompletionPortThreads);
            Assert.Equal(value.MaxWorkerThreads - value.AvailableWorkerThreads, value.UsingWorkerThreads);
            Assert.Equal(value.MaxCompletionPortThreads - value.AvailableCompletionPortThreads, value.UsingCompletionPortThreads);
        });
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
