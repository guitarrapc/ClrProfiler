using ClrProfiler.DatadogTracing;
using ClrProfiler.Statistics;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace CleProfiler.DatadogTracing.UnitTest;

/// <summary>
/// Asserts that the metric projection hot paths allocate nothing once they are warm.
/// </summary>
/// <remarks>
/// Every measured loop lives in its own <see cref="MethodImplOptions.AggressiveOptimization"/>
/// method, and warmup runs the same method the measurement does. Without that, the loop starts as
/// tier-0 code and the runtime swaps it mid-flight through an on-stack replacement transition,
/// which allocates 24 bytes on the executing thread. Whether that transition lands in the warmup
/// loop or inside the measurement window depends on how busy the runtime was when the test
/// started, so it made these assertions depend on test execution order. AggressiveOptimization
/// opts the loop out of tiered compilation, so it is fully optimized on the first call and never
/// transitions. Verified: with the loop left tiered, the allocation reproduces at a fixed
/// iteration even when the loop body is replaced by integer arithmetic.
/// </remarks>
public class MetricTagAllocationTest
{
    private const int WarmupIterations = 1_000;
    private const int Iterations = 10_000;
    private static readonly ILogger DisabledLogger = new DisabledLoggerImplementation();
    private static readonly Exception CallbackException = new InvalidOperationException("callback failed");
    private static readonly GCStartEndStatistics Statistics = new(1, 0, 2, 1, 1.5, 100, 200);
    private const string KnownProfilerName = nameof(ClrProfiler.ProcessInfoTimerProfiler);
    // Worst case for the bounded lookup: scans every known name, then falls back to unknown.
    private const string UnknownProfilerName = "SomeCustomProfiler";

    [Test]
    public async Task LoggerGcEventStartEnd_WhenDebugDisabled_DoesNotAllocate()
    {
        LogGcEvents(WarmupIterations);

        var before = GC.GetAllocatedBytesForCurrentThread();
        LogGcEvents(Iterations);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"Allocated bytes: {allocatedBytes}; bytes/call: {(double)allocatedBytes / Iterations:N2}");
        await Assert.That(allocatedBytes).IsEqualTo(0);
    }

    [Test]
    public async Task MetricTagLookup_DoesNotAllocateAfterInitialization()
    {
        LookUpTags(WarmupIterations);

        var before = GC.GetAllocatedBytesForCurrentThread();
        LookUpTags(Iterations);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocatedBytes).IsEqualTo(0);
    }

    [Test]
    public async Task CallbackExceptionLogging_WhenCriticalDisabled_DoesNotAllocate()
    {
        var datadogHandler = new DatadogTrackerCallbackHandler(DisabledLogger);
        var loggerHandler = new LoggerTrackerCallbackHandler(DisabledLogger);

        ReportCallbackExceptions(datadogHandler, loggerHandler, WarmupIterations);

        var before = GC.GetAllocatedBytesForCurrentThread();
        ReportCallbackExceptions(datadogHandler, loggerHandler, Iterations);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocatedBytes).IsEqualTo(0);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void LogGcEvents(int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            LoggerTracing.GcEventStartEnd(Statistics, DisabledLogger);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void LookUpTags(int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            UseAllTagKinds();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ReportCallbackExceptions(DatadogTrackerCallbackHandler datadogHandler, LoggerTrackerCallbackHandler loggerHandler, int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            datadogHandler.OnException(CallbackException);
            loggerHandler.OnException(CallbackException);
        }
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
        ref readonly var knownProfiler = ref MetricTags.GetProfiler(KnownProfilerName);
        ref readonly var unknownProfiler = ref MetricTags.GetProfiler(UnknownProfilerName);

        GC.KeepAlive(contention.Text);
        GC.KeepAlive(gc.Text);
        GC.KeepAlive(suspend.Text);
        GC.KeepAlive(thread.Text);
        GC.KeepAlive(gcInfo.Loh.Text);
        GC.KeepAlive(knownProfiler.Text);
        GC.KeepAlive(unknownProfiler.Text);
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
