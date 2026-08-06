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

        await Assert.That(logger.Messages).Count().IsEqualTo(2);
        foreach (var message in logger.Messages)
        {
            await Assert.That(message).Contains($"tags: contention_type:{flag}");
        }
    }

    [Test]
    public async Task ContentionEventStartEnd_ProjectsAggregatedCountAndAverageDuration()
    {
        var logger = new CapturingLogger();

        LoggerTracing.ContentionEventStartEnd(new ContentionEventStatistics(1, 0, 30, 3), logger);

        await Assert.That(logger.Messages[0]).Contains("startend_count: 3,");
        await Assert.That(logger.Messages[1]).Contains("startend_duration_ns: 10,");
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

        await Assert.That(logger.Messages).Count().IsEqualTo(2);
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

        await Assert.That(logger.Messages).Count().IsEqualTo(2);
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

        await Assert.That(logger.Messages).Count().IsEqualTo(2);
        foreach (var message in logger.Messages)
        {
            await Assert.That(message).Contains($"tags: thread_adjust_reason:{reasonText}");
        }
    }

    [Test]
    public async Task UnknownRuntimeValues_UseBoundedUnknownTags()
    {
        var logger = new CapturingLogger();

        LoggerTracing.ContentionEventStartEnd(new ContentionEventStatistics(1, byte.MaxValue, 2.5), logger);
        LoggerTracing.GcEventStartEnd(new GCStartEndStatistics(1, uint.MaxValue, uint.MaxValue, uint.MaxValue, 1.5, 100, 200), logger);
        LoggerTracing.GcEventSuspend(new GCSuspendStatistics(1.5, uint.MaxValue, 3), logger);
        LoggerTracing.ThreadPoolEventAdjustment(new ThreadPoolAdjustmentStatistics(1, 2.5, 3, uint.MaxValue), logger);
        LoggerTracing.GcInfoTimerGauge(new GCInfoStatistics(
            DateTime.UnixEpoch,
            (GCMode)int.MaxValue,
            (GCLargeObjectHeapCompactionMode)int.MaxValue,
            (GCLatencyMode)int.MaxValue,
            1,
            2,
            3,
            4,
            5,
            6,
            7,
            8,
            9,
            10), logger);

        await Assert.That(logger.Messages).Contains(message => message.Contains("tags: contention_type:unknown"));
        await Assert.That(logger.Messages).Contains(message => message.Contains("tags: gc_gen:unknown,gc_type:unknown,gc_reason:unknown"));
        await Assert.That(logger.Messages).Contains(message => message.Contains("tags: gc_suspend_reason:unknown"));
        await Assert.That(logger.Messages).Contains(message => message.Contains("tags: thread_adjust_reason:unknown"));
        await Assert.That(logger.Messages).Contains(message => message.Contains("tags: gc_mode:unknown,latency_mode:unknown,compaction_mode:unknown"));
    }

    [Test]
    public async Task PrecomputedGcTags_ReuseSharedComponentStrings()
    {
        ref readonly var first = ref MetricTags.GetGcStartEnd(2, 0, 1);
        ref readonly var second = ref MetricTags.GetGcStartEnd(2, 1, 1);
        var firstGeneration = first.Values[0];
        var secondGeneration = second.Values[0];
        var firstReason = first.Values[2];
        var secondReason = second.Values[2];

        await Assert.That(ReferenceEquals(firstGeneration, secondGeneration)).IsTrue();
        await Assert.That(ReferenceEquals(firstReason, secondReason)).IsTrue();
    }

    [Test]
    public async Task PrecomputedReasonTags_StayAlignedWithStatisticsMappings()
    {
        for (uint reason = 0; reason <= 10; reason++)
        {
            var statistics = new GCStartEndStatistics(0, 0, 0, reason, 0, 0, 0);
            var tags = MetricTags.GetGcStartEnd(0, 0, reason);
            await Assert.That(tags.Values[2]).IsEqualTo($"gc_reason:{statistics.GetReasonString()}");
        }

        for (uint reason = 0; reason <= 6; reason++)
        {
            var statistics = new GCSuspendStatistics(0, reason, 0);
            var tags = MetricTags.GetGcSuspend(reason);
            await Assert.That(tags.Values[0]).IsEqualTo($"gc_suspend_reason:{statistics.GetReasonString()}");
        }

        for (uint reason = 0; reason <= 8; reason++)
        {
            var statistics = new ThreadPoolAdjustmentStatistics(0, 0, 0, reason);
            var tags = MetricTags.GetThreadAdjustment(reason);
            await Assert.That(tags.Values[0]).IsEqualTo($"thread_adjust_reason:{statistics.GetReasonString()}");
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
