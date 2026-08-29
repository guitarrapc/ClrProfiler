using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;

namespace ClrProfiler.UnitTest;

/// <summary>
/// The configured error callback is user code and can itself throw. A throwing error callback
/// must never terminate a reader loop (which would silently stop all delivery for that listener)
/// and must never propagate out of an event or timer producer (which would take down the
/// EventPipe dispatch thread or crash the process from a timer thread).
/// </summary>
[NotInParallel]
public class ErrorCallbackResilienceTest
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task GCEventReaderSurvivesThrowingErrorCallback()
    {
        var delivered = new List<GCEventStatistics>(2);
        var secondDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitCalls = 0;
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableGCEventListener(value =>
        {
            if (Interlocked.Increment(ref emitCalls) == 1)
            {
                return Task.FromException(new InvalidOperationException("emit failed"));
            }
            delivered.Add(value);
            secondDelivered.TrySetResult();
            return Task.CompletedTask;
        }, _ => throw new InvalidOperationException("error callback failed"));

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        try
        {
            listener.ProcessEvent("GCSuspendEEBegin_V1", origin, [1U, 1U]);
            listener.ProcessEvent("GCRestartEEEnd_V1", origin.AddTicks(1), []);
            listener.ProcessEvent("GCSuspendEEBegin_V1", origin.AddTicks(2), [1U, 2U]);
            listener.ProcessEvent("GCRestartEEEnd_V1", origin.AddTicks(3), []);

            await secondDelivered.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        var survivor = await Assert.That(delivered).HasSingleItem();
        await Assert.That(survivor.GCSuspendStatistics.Count).IsEqualTo(2U);
    }

    [Test]
    public async Task ContentionReaderSurvivesThrowingErrorCallback()
    {
        var deliveredCount = 0L;
        var secondDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emitCalls = 0;
        using var cts = new CancellationTokenSource(TestTimeout);
        using var listener = new TestableContentionEventListener(value =>
        {
            if (Interlocked.Increment(ref emitCalls) == 1)
            {
                return Task.FromException(new InvalidOperationException("emit failed"));
            }
            Interlocked.Add(ref deliveredCount, value.Count);
            secondDelivered.TrySetResult();
            return Task.CompletedTask;
        }, _ => throw new InvalidOperationException("error callback failed"));

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        listener.EnableReading();
        var readerTask = listener.OnReadResultAsync(cts.Token).AsTask();
        try
        {
            listener.ProcessEvent("ContentionStop_V1", origin, [(byte)0, 0U, 5D]);
            // Wait until the first (failing) emit was attempted before producing the second
            // window, so the two events cannot fold into one aggregation window.
            while (Volatile.Read(ref emitCalls) == 0)
            {
                await Task.Delay(10, TestContext.Current!.Execution.CancellationToken);
            }
            listener.ProcessEvent("ContentionStop_V1", origin.AddTicks(1), [(byte)0, 0U, 7D]);

            await secondDelivered.Task.WaitAsync(TestTimeout, TestContext.Current!.Execution.CancellationToken);
        }
        finally
        {
            await cts.CancelAsync();
            await readerTask;
        }

        await Assert.That(Volatile.Read(ref deliveredCount)).IsEqualTo(1L);
    }

    [Test]
    public async Task GCEventProducerDoesNotPropagateThrowingErrorCallback()
    {
        using var listener = new TestableGCEventListener(
            _ => Task.CompletedTask,
            _ => throw new InvalidOperationException("error callback failed"));
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Malformed payload routes into the error callback; its exception must not escape into
        // the EventPipe dispatch thread that calls ProcessEvent in production.
        void Act() => listener.ProcessEvent("GCEnd_V1", origin, []);

        await Assert.That(Act).ThrowsNothing();
    }

    [Test]
    public async Task ThreadPoolProducerDoesNotPropagateThrowingErrorCallback()
    {
        using var listener = new TestableThreadPoolEventListener(
            _ => Task.CompletedTask,
            _ => throw new InvalidOperationException("error callback failed"));
        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        void Act() => listener.ProcessEvent("ThreadPoolWorkerThreadAdjustmentAdjustment", origin, []);

        await Assert.That(Act).ThrowsNothing();
    }

    private sealed class TestableGCEventListener(Func<GCEventStatistics, Task> onEventEmit, Action<Exception> onEventError)
        : GCEventListener(onEventEmit, onEventError)
    {
        public void EnableReading() => Enabled = true;
    }

    private sealed class TestableContentionEventListener(Func<ContentionEventStatistics, Task> onEventEmit, Action<Exception> onEventError)
        : ContentionEventListener(onEventEmit, onEventError)
    {
        public void EnableReading() => Enabled = true;
    }

    private sealed class TestableThreadPoolEventListener(Func<ThreadPoolEventStatistics, Task> onEventEmit, Action<Exception> onEventError)
        : ThreadPoolEventListener(onEventEmit, onEventError)
    {
    }
}
