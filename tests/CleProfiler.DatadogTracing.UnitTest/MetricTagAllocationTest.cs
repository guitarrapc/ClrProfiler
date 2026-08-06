using ClrProfiler.DatadogTracing;
using ClrProfiler.Statistics;
using Microsoft.Extensions.Logging;

namespace CleProfiler.DatadogTracing.UnitTest;

public class MetricTagAllocationTest
{
    private const int Iterations = 10_000;
    private static readonly ILogger DisabledLogger = new DisabledLoggerImplementation();
    private static readonly Exception CallbackException = new InvalidOperationException("callback failed");
    private static readonly GCStartEndStatistics Statistics = new(1, 0, 2, 1, 1.5, 100, 200);

    [Test]
    public async Task LoggerGcEventStartEnd_WhenDebugDisabled_DoesNotAllocate()
    {
        for (var i = 0; i < 1_000; i++)
        {
            LoggerTracing.GcEventStartEnd(Statistics, DisabledLogger);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            LoggerTracing.GcEventStartEnd(Statistics, DisabledLogger);
        }
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"Allocated bytes: {allocatedBytes}; bytes/call: {(double)allocatedBytes / Iterations:N2}");
        await Assert.That(allocatedBytes).IsEqualTo(0);
    }

    [Test]
    public async Task MetricTagLookup_DoesNotAllocateAfterInitialization()
    {
        UseAllTagKinds();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            UseAllTagKinds();
        }
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocatedBytes).IsEqualTo(0);
    }

    [Test]
    public async Task CallbackExceptionLogging_WhenCriticalDisabled_DoesNotAllocate()
    {
        var datadogHandler = new DatadogTrackerCallbackHandler(DisabledLogger);
        var loggerHandler = new LoggerTrackerCallbackHandler(DisabledLogger);

        for (var i = 0; i < 1_000; i++)
        {
            datadogHandler.OnException(CallbackException);
            loggerHandler.OnException(CallbackException);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            datadogHandler.OnException(CallbackException);
            loggerHandler.OnException(CallbackException);
        }
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocatedBytes).IsEqualTo(0);
    }

    private static void UseAllTagKinds()
    {
        ref readonly var contention = ref MetricTags.GetContention(1);
        ref readonly var gc = ref MetricTags.GetGcStartEnd(2, 1, 10);
        ref readonly var suspend = ref MetricTags.GetGcSuspend(6);
        ref readonly var thread = ref MetricTags.GetThreadAdjustment(8);
        ref readonly var gcInfo = ref MetricTags.GetGcInfo(
            GCMode.Server,
            System.Runtime.GCLatencyMode.SustainedLowLatency,
            System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce);

        GC.KeepAlive(contention.Text);
        GC.KeepAlive(gc.Text);
        GC.KeepAlive(suspend.Text);
        GC.KeepAlive(thread.Text);
        GC.KeepAlive(gcInfo.Loh.Text);
    }

    private sealed class DisabledLoggerImplementation : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
