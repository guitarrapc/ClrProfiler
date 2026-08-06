using ClrProfiler.Statistics;
using Microsoft.Extensions.Logging;

namespace ClrProfiler.DatadogTracing;

public static partial class LoggerTracing
{
    public static void ContentionEventStartEnd(in ContentionEventStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetContention(statistics.Flag);
        LogLongMetric(logger, MetricNames.Event.ContentionStartEndCount, statistics.Count, tags.Text);
        LogDoubleMetric(logger, MetricNames.Event.ContentionStartEndDurationNsSum, statistics.DurationNsSum, tags.Text);
        LogDoubleMetric(logger, MetricNames.Event.ContentionStartEndDurationNsMax, statistics.DurationNsMax, tags.Text);
    }

    public static void GcEventStartEnd(in GCStartEndStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetGcStartEnd(statistics.Generation, statistics.Type, statistics.Reason);
        LogIntMetric(logger, MetricNames.Event.GcStartEndCount, 1, tags.Text);
        LogDoubleMetric(logger, MetricNames.Event.GcStartEndDurationMs, statistics.DurationMillsec, tags.Text);
    }

    public static void GcEventSuspend(in GCSuspendStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetGcSuspend(statistics.Reason);
        LogUIntMetric(logger, MetricNames.Event.GcSuspendObjectCount, statistics.Count, tags.Text);
        LogDoubleMetric(logger, MetricNames.Event.GcSuspendDurationMs, statistics.DurationMillisec, tags.Text);
    }

    public static void ThreadPoolEventWorker(in ThreadPoolWorkerStatistics statistics, ILogger logger)
    {
        LogUIntMetric(logger, MetricNames.Event.ThreadPoolAvailableWorkerThreadCount, statistics.ActiveWrokerThreads, string.Empty);
    }

    public static void ThreadPoolEventAdjustment(in ThreadPoolAdjustmentStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetThreadAdjustment(statistics.Reason);
        LogDoubleMetric(logger, MetricNames.Event.ThreadPoolAdjustmentAverageThroughput, statistics.AverageThrouput, tags.Text);
        LogUIntMetric(logger, MetricNames.Event.ThreadPoolAdjustmentNewWorkerThreadsCount, statistics.NewWorkerThreads, tags.Text);
    }

    public static void ThreadPoolStarvationEventAdjustment(in ThreadPoolAdjustmentStatistics statistics, ILogger logger)
    {
        LogThreadPoolStarvation(logger);
    }

    public static void GcInfoTimerGauge(in GCInfoStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetGcInfo(statistics.GCMode, statistics.LatencyMode, statistics.CompactionMode);
        LogLongMetric(logger, MetricNames.Timer.GcHeapSizeBytes, statistics.HeapSize, tags.Base.Text);
        LogLongMetric(logger, MetricNames.Timer.GcTotalAllocationBytes, statistics.TotalAllocationBytes, tags.Base.Text);
        LogIntMetric(logger, MetricNames.Timer.GcCount, statistics.Gen0Count, tags.Gen0.Text);
        LogIntMetric(logger, MetricNames.Timer.GcCount, statistics.Gen1Count, tags.Gen1.Text);
        LogIntMetric(logger, MetricNames.Timer.GcCount, statistics.Gen2Count, tags.Gen2.Text);
        LogULongMetric(logger, MetricNames.Timer.GcSize, statistics.Gen0Size, tags.Gen0.Text);
        LogULongMetric(logger, MetricNames.Timer.GcSize, statistics.Gen1Size, tags.Gen1.Text);
        LogULongMetric(logger, MetricNames.Timer.GcSize, statistics.Gen2Size, tags.Gen2.Text);
        LogULongMetric(logger, MetricNames.Timer.GcSize, statistics.LohSize, tags.Loh.Text);
        LogIntMetric(logger, MetricNames.Timer.GcTimeInGcPercent, statistics.TimeInGc, tags.Base.Text);
    }

    public static void ProcessInfoTimerGauge(in ProcessInfoStatistics statistics, ILogger logger)
    {
        LogDoubleMetric(logger, MetricNames.Timer.ProcessCpu, statistics.Cpu, string.Empty);
        LogLongMetric(logger, MetricNames.Timer.ProcessPrivateBytes, statistics.PrivateBytes, string.Empty);
        LogLongMetric(logger, MetricNames.Timer.ProcessWorkingSets, statistics.WorkingSet, string.Empty);
    }

    public static void ThreadInfoTimerGauge(in ThreadInfoStatistics statistics, ILogger logger)
    {
        LogIntMetric(logger, MetricNames.Timer.ThreadAvailableWorkerThreads, statistics.AvailableWorkerThreads, string.Empty);
        LogIntMetric(logger, MetricNames.Timer.ThreadAvailableCompletionPortThreads, statistics.AvailableCompletionPortThreads, string.Empty);
        LogIntMetric(logger, MetricNames.Timer.ThreadMaxWorkerThreads, statistics.MaxWorkerThreads, string.Empty);
        LogIntMetric(logger, MetricNames.Timer.ThreadMaxCompletionPortThreads, statistics.MaxCompletionPortThreads, string.Empty);
        LogIntMetric(logger, MetricNames.Timer.ThreadUsingWorkerThreads, statistics.UsingWorkerThreads, string.Empty);
        LogIntMetric(logger, MetricNames.Timer.ThreadUsingCompletionPortThreads, statistics.UsingCompletionPortThreads, string.Empty);
        LogIntMetric(logger, MetricNames.Timer.ThreadCount, statistics.ThreadCount, string.Empty);
        LogLongMetric(logger, MetricNames.Timer.ThreadQueueLength, statistics.QueueLength, string.Empty);
        LogLongMetric(logger, MetricNames.Timer.ThreadLockContentionCount, statistics.LockContentionCount, string.Empty);
        LogLongMetric(logger, MetricNames.Timer.ThreadCompletedItemsCount, statistics.CompletedItemsCount, string.Empty);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{MetricName}: {Value}, tags: {Tags}")]
    private static partial void LogIntMetric(ILogger logger, string metricName, int value, string tags);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{MetricName}: {Value}, tags: {Tags}")]
    private static partial void LogUIntMetric(ILogger logger, string metricName, uint value, string tags);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{MetricName}: {Value}, tags: {Tags}")]
    private static partial void LogLongMetric(ILogger logger, string metricName, long value, string tags);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{MetricName}: {Value}, tags: {Tags}")]
    private static partial void LogULongMetric(ILogger logger, string metricName, ulong value, string tags);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{MetricName}: {Value}, tags: {Tags}")]
    private static partial void LogDoubleMetric(ILogger logger, string metricName, double value, string tags);

    [LoggerMessage(Level = LogLevel.Debug, Message = "title: ThreadPool Starvation detected, text: .NET CLR automatically expanding ThreadPool, but this results slow down system. Watch out for error increase and take action to expan thread pool in advance., alertType: warning, aggregationKey: host")]
    private static partial void LogThreadPoolStarvation(ILogger logger);
}
