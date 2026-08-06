using ClrProfiler.DatadogTracing;
using ClrProfiler.Statistics;
using Microsoft.Extensions.Logging;
using System.Runtime;

namespace CleProfiler.DatadogTracing.UnitTest;

public class MetricTagProjectionTest
{
    [Test]
    [Arguments((byte)0)]
    [Arguments((byte)1)]
    public async Task ContentionEventStartEnd_PreservesTags(byte flag)
    {
        var logger = new CapturingLogger();

        LoggerTracing.ContentionEventStartEnd(new ContentionEventStatistics(1, flag, 2.5), logger);

        foreach (var message in logger.Messages)
        {
            await Assert.That(message).Contains($"tags: contention_type:{flag}");
        }
    }

    [Test]
    [Arguments(0u, "soh")]
    [Arguments(1u, "induced")]
    [Arguments(2u, "low_memory")]
    [Arguments(3u, "empty")]
    [Arguments(4u, "loh")]
    [Arguments(5u, "oos_soh")]
    [Arguments(6u, "oos_loh")]
    [Arguments(7u, "incuded_non_forceblock")]
    [Arguments(8u, "stress_testing")]
    [Arguments(9u, "finalizer_low_memory_induced")]
    [Arguments(10u, "user_gc_request")]
    public async Task GcEventStartEnd_PreservesTags(uint reason, string reasonText)
    {
        var logger = new CapturingLogger();

        LoggerTracing.GcEventStartEnd(new GCStartEndStatistics(1, 1, 2, reason, 1.5, 100, 200), logger);

        foreach (var message in logger.Messages)
        {
            await Assert.That(message).Contains($"tags: gc_gen:2,gc_type:1,gc_reason:{reasonText}");
        }
    }

    [Test]
    [Arguments(0u, "other")]
    [Arguments(1u, "gc")]
    [Arguments(2u, "appdomain_shudown")]
    [Arguments(3u, "code_pitch")]
    [Arguments(4u, "shutdown")]
    [Arguments(5u, "debugger")]
    [Arguments(6u, "prep_gc")]
    public async Task GcEventSuspend_PreservesTags(uint reason, string reasonText)
    {
        var logger = new CapturingLogger();

        LoggerTracing.GcEventSuspend(new GCSuspendStatistics(1.5, reason, 3), logger);

        foreach (var message in logger.Messages)
        {
            await Assert.That(message).Contains($"tags: gc_suspend_reason:{reasonText}");
        }
    }

    [Test]
    [Arguments(0u, "warmup")]
    [Arguments(1u, "initializing")]
    [Arguments(2u, "random_move")]
    [Arguments(3u, "climbing_move")]
    [Arguments(4u, "change_point")]
    [Arguments(5u, "stabilizing")]
    [Arguments(6u, "starvation")]
    [Arguments(7u, "timedout")]
    [Arguments(8u, "cooperative_blocking")]
    public async Task ThreadPoolEventAdjustment_PreservesTags(uint reason, string reasonText)
    {
        var logger = new CapturingLogger();

        LoggerTracing.ThreadPoolEventAdjustment(new ThreadPoolAdjustmentStatistics(1, 2.5, 3, reason), logger);

        foreach (var message in logger.Messages)
        {
            await Assert.That(message).Contains($"tags: thread_adjust_reason:{reasonText}");
        }
    }

    [Test]
    public async Task GcInfoTimerGauge_PreservesBaseAndGenerationTagOrder()
    {
        var logger = new CapturingLogger();
        var statistics = new GCInfoStatistics(
            DateTime.UnixEpoch,
            GCMode.Server,
            GCLargeObjectHeapCompactionMode.CompactOnce,
            GCLatencyMode.SustainedLowLatency,
            1,
            2,
            3,
            4,
            5,
            6,
            7,
            8,
            9,
            10);

        LoggerTracing.GcInfoTimerGauge(statistics, logger);

        await Assert.That(logger.Messages[0]).Contains("tags: gc_mode:Server,latency_mode:SustainedLowLatency,compaction_mode:CompactOnce");
        await Assert.That(logger.Messages[2]).Contains("tags: gc_gen:0,gc_mode:Server,latency_mode:SustainedLowLatency,compaction_mode:CompactOnce");
        await Assert.That(logger.Messages[8]).Contains("tags: gc_gen:loh,gc_mode:Server,latency_mode:SustainedLowLatency,compaction_mode:CompactOnce");
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
