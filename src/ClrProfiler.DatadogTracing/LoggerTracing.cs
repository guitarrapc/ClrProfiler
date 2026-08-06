using ClrProfiler.Statistics;
using Microsoft.Extensions.Logging;

namespace ClrProfiler.DatadogTracing;

public static partial class LoggerTracing
{
    public static void ContentionEventStartEnd(in ContentionEventStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetContention(statistics.Flag);
        LogIntMetric(logger, "clr_diagnostics_event.contention.startend_count", 1, tags.Text);
        LogDoubleMetric(logger, "clr_diagnostics_event.contention.startend_duration_ns", statistics.DurationNs, tags.Text);
    }

    public static void GcEventStartEnd(in GCStartEndStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetGcStartEnd(statistics.Generation, statistics.Type, statistics.Reason);
        LogIntMetric(logger, "clr_diagnostics_event.gc.startend_count", 1, tags.Text);
        LogDoubleMetric(logger, "clr_diagnostics_event.gc.startend_duration_ms", statistics.DurationMillsec, tags.Text);
    }

    public static void GcEventSuspend(in GCSuspendStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetGcSuspend(statistics.Reason);
        LogUIntMetric(logger, "clr_diagnostics_event.gc.suspend_object_count", statistics.Count, tags.Text);
        LogDoubleMetric(logger, "clr_diagnostics_event.gc.suspend_duration_ms", statistics.DurationMillisec, tags.Text);
    }

    public static void ThreadPoolEventWorker(in ThreadPoolWorkerStatistics statistics, ILogger logger)
    {
        LogUIntMetric(logger, "clr_diagnostics_event.threadpool.available_workerthread_count", statistics.ActiveWrokerThreads, string.Empty);
    }

    public static void ThreadPoolEventAdjustment(in ThreadPoolAdjustmentStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetThreadAdjustment(statistics.Reason);
        LogDoubleMetric(logger, "clr_diagnostics_event.threadpool.adjustment_avg_throughput", statistics.AverageThrouput, tags.Text);
        LogUIntMetric(logger, "clr_diagnostics_event.threadpool.adjustment_new_workerthreads_count", statistics.NewWorkerThreads, tags.Text);
    }

    public static void ThreadPoolStarvationEventAdjustment(in ThreadPoolAdjustmentStatistics statistics, ILogger logger)
    {
        LogThreadPoolStarvation(logger);
    }

    public static void GcInfoTimerGauge(in GCInfoStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetGcInfo(statistics.GCMode, statistics.LatencyMode, statistics.CompactionMode);
        LogLongMetric(logger, "clr_diagnostics_timer.gc.heap_size_bytes", statistics.HeapSize, tags.Base.Text);
        LogLongMetric(logger, "clr_diagnostics_timer.gc.total_allocation_bytes", statistics.TotalAllocationBytes, tags.Base.Text);
        LogIntMetric(logger, "clr_diagnostics_timer.gc.gc_count", statistics.Gen0Count, tags.Gen0.Text);
        LogIntMetric(logger, "clr_diagnostics_timer.gc.gc_count", statistics.Gen1Count, tags.Gen1.Text);
        LogIntMetric(logger, "clr_diagnostics_timer.gc.gc_count", statistics.Gen2Count, tags.Gen2.Text);
        LogULongMetric(logger, "clr_diagnostics_timer.gc.gc_size", statistics.Gen0Size, tags.Gen0.Text);
        LogULongMetric(logger, "clr_diagnostics_timer.gc.gc_size", statistics.Gen1Size, tags.Gen1.Text);
        LogULongMetric(logger, "clr_diagnostics_timer.gc.gc_size", statistics.Gen2Size, tags.Gen2.Text);
        LogULongMetric(logger, "clr_diagnostics_timer.gc.gc_size", statistics.LohSize, tags.Loh.Text);
        LogIntMetric(logger, "clr_diagnostics_timer.gc.time_in_gc_percent", statistics.TimeInGc, tags.Base.Text);
    }

    public static void ProcessInfoTimerGauge(in ProcessInfoStatistics statistics, ILogger logger)
    {
        LogDoubleMetric(logger, "clr_diagnostics_timer.process.cpu", statistics.Cpu, string.Empty);
        LogLongMetric(logger, "clr_diagnostics_timer.process.private_bytes", statistics.PrivateBytes, string.Empty);
        LogLongMetric(logger, "clr_diagnostics_timer.process.working_sets", statistics.WorkingSet, string.Empty);
    }

    public static void ThreadInfoTimerGauge(in ThreadInfoStatistics statistics, ILogger logger)
    {
        LogIntMetric(logger, "clr_diagnostics_timer.thread.available_worker_threads", statistics.AvailableWorkerThreads, string.Empty);
        LogIntMetric(logger, "clr_diagnostics_timer.thread.available_completion_port_threads", statistics.AvailableCompletionPortThreads, string.Empty);
        LogIntMetric(logger, "clr_diagnostics_timer.thread.max_worker_threads", statistics.MaxWorkerThreads, string.Empty);
        LogIntMetric(logger, "clr_diagnostics_timer.thread.max_completion_port_threads", statistics.MaxCompletionPortThreads, string.Empty);
        LogIntMetric(logger, "clr_diagnostics_timer.thread.using_worker_threads", statistics.UsingWorkerThreads, string.Empty);
        LogIntMetric(logger, "clr_diagnostics_timer.thread.using_completion_port_threads", statistics.UsingCompletionPortThreads, string.Empty);
        LogIntMetric(logger, "clr_diagnostics_timer.thread.thread_count", statistics.ThreadCount, string.Empty);
        LogLongMetric(logger, "clr_diagnostics_timer.thread.queue_length", statistics.QueueLength, string.Empty);
        LogLongMetric(logger, "clr_diagnostics_timer.thread.lock_contention_count", statistics.LockContentionCount, string.Empty);
        LogLongMetric(logger, "clr_diagnostics_timer.thread.completed_items_count", statistics.CompletedItemsCount, string.Empty);
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
