using ClrProfiler.Statistics;
using Microsoft.Extensions.Logging;

namespace ClrProfiler.DatadogTracing;

public static class LoggerTracing
{
    /// list of event tags
    /// - clr_diagnostics_event.contention.startend_count"
    /// - clr_diagnostics_event.contention.startend_duration_ns"
    ///     contention_flag|managed|native
    /// - clr_diagnostics_event.gc.startend_count            // count
    /// - clr_diagnostics_event.gc.startend_duration_ms      // gauge
    ///     gc_gen:0|1|2
    ///     gc_type:blocking_outsite|background|blocking_during_gc
    ///     gc_reason:soh|induced|low_memory|empty|loh|oos_soh|oos_loh|incuded_non_forceblock
    /// - clr_diagnostics_event.gc.suspend_count             // count
    /// - clr_diagnostics_event.gc.suspend_duration_ms       // gauge
    ///     gc_suspend_reason:other|gc|appdomain_shudown|code_pitch|shutdown|debugger|prep_gc
    /// - clr_diagnostics_event.threadpool.available_workerthread_count // gauge
    /// - clr_diagnostics_event.threadpool.adjustment_avg_throughput // gauge
    /// - clr_diagnostics_event.threadpool.adjustment_new_workerthreads_count // gauge
    ///     threadpool_adjustment_reason:warmup|initializing|random_move|climbing_move|change_point|stabilizing|starvation|timeout

    // ContentionEvent
    public static void ContentionEventStartEnd(in ContentionEventStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetContention(statistics.Flag);
        logger.LogDebug($"clr_diagnostics_event.contention.startend_count: 1, tags: {tags.Text}");
        logger.LogDebug($"clr_diagnostics_event.contention.startend_duration_ns: {statistics.DurationNs}, tags: {tags.Text}");
    }

    // GCEvent
    public static void GcEventStartEnd(in GCStartEndStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetGcStartEnd(statistics.Generation, statistics.Type, statistics.Reason);
        logger.LogDebug($"clr_diagnostics_event.gc.startend_count: 1, tags: {tags.Text}");
        logger.LogDebug($"clr_diagnostics_event.gc.startend_duration_ms: {statistics.DurationMillsec}, tags: {tags.Text}");
    }

    public static void GcEventSuspend(in GCSuspendStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetGcSuspend(statistics.Reason);
        logger.LogDebug($"clr_diagnostics_event.gc.suspend_object_count: {statistics.Count}, tags: {tags.Text}");
        logger.LogDebug($"clr_diagnostics_event.gc.suspend_duration_ms: {statistics.DurationMillisec}, tags: {tags.Text}");
    }

    // ThreadPoolEvent
    public static void ThreadPoolEventWorker(in ThreadPoolWorkerStatistics statistics, ILogger logger)
    {
        logger.LogDebug($"clr_diagnostics_event.threadpool.available_workerthread_count: {statistics.ActiveWrokerThreads}, tags: ");
    }
    public static void ThreadPoolEventAdjustment(in ThreadPoolAdjustmentStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetThreadAdjustment(statistics.Reason);
        logger.LogDebug($"clr_diagnostics_event.threadpool.adjustment_avg_throughput: {statistics.AverageThrouput}, tags: {tags.Text}");
        logger.LogDebug($"clr_diagnostics_event.threadpool.adjustment_new_workerthreads_count: {statistics.NewWorkerThreads}, tags: {tags.Text}");
    }
    public static void ThreadPoolStarvationEventAdjustment(in ThreadPoolAdjustmentStatistics statistics, ILogger logger)
    {
        logger.LogDebug($"title: ThreadPool Starvation detected, text: .NET CLR automatically expanding ThreadPool, but this results slow down system. Watch out for error increase and take action to expan thread pool in advance., alertType: warning, aggregationKey: host");
    }


    /// list of timer tags
    /// - clr_diagnostics_timer.gc.heap_size // gauge
    /// - clr_diagnostics_timer.gc.total_allocation_bytes // gauge
    /// - clr_diagnostics_timer.gc.gen0_count // gauge
    /// - clr_diagnostics_timer.gc.gen1_count // gauge
    /// - clr_diagnostics_timer.gc.gen2_count // gauge
    /// - clr_diagnostics_timer.gc.gen0_size // gauge
    /// - clr_diagnostics_timer.gc.gen1_size // gauge
    /// - clr_diagnostics_timer.gc.gen2_size // gauge
    /// - clr_diagnostics_timer.gc.loh_size // gauge
    /// - clr_diagnostics_timer.gc.time_in_gc_percent // gauge
    /// - clr_diagnostics_timer.process.cpu // gauge
    /// - clr_diagnostics_timer.process.private_bytes // gauge
    /// - clr_diagnostics_timer.process.working_sets // gauge
    /// - clr_diagnostics_timer.thread.available_worker_threads // gauge
    /// - clr_diagnostics_timer.thread.available_completion_port_threads // gauge
    /// - clr_diagnostics_timer.thread.max_worker_threads // gauge
    /// - clr_diagnostics_timer.thread.max_completion_port_threads // gauge
    /// - clr_diagnostics_timer.thread.using_worker_threads // gauge
    /// - clr_diagnostics_timer.thread.using_completion_port_threads // gauge
    /// - clr_diagnostics_timer.thread.thread_count // gauge
    /// - clr_diagnostics_timer.thread.queue_length // gauge
    /// - clr_diagnostics_timer.thread.lock_contention_count // gauge
    /// - clr_diagnostics_timer.thread.completed_items_count // gauge
    /// - clr_diagnostics_timer.thread.available_completion_port_threads // gauge

    // GC
    public static void GcInfoTimerGauge(in GCInfoStatistics statistics, ILogger logger)
    {
        ref readonly var tags = ref MetricTags.GetGcInfo(statistics.GCMode, statistics.LatencyMode, statistics.CompactionMode);
        logger.LogDebug($"clr_diagnostics_timer.gc.heap_size_bytes: {statistics.HeapSize}, tags: {tags.Base.Text}");
        logger.LogDebug($"clr_diagnostics_timer.gc.total_allocation_bytes: {statistics.TotalAllocationBytes}, tags: {tags.Base.Text}");
        logger.LogDebug($"clr_diagnostics_timer.gc.gc_count: {statistics.Gen0Count}, tags: {tags.Gen0.Text}");
        logger.LogDebug($"clr_diagnostics_timer.gc.gc_count: {statistics.Gen1Count}, tags: {tags.Gen1.Text}");
        logger.LogDebug($"clr_diagnostics_timer.gc.gc_count: {statistics.Gen2Count}, tags: {tags.Gen2.Text}");
        logger.LogDebug($"clr_diagnostics_timer.gc.gc_size: {statistics.Gen0Size}, tags: {tags.Gen0.Text}");
        logger.LogDebug($"clr_diagnostics_timer.gc.gc_size: {statistics.Gen1Size}, tags: {tags.Gen1.Text}");
        logger.LogDebug($"clr_diagnostics_timer.gc.gc_size: {statistics.Gen2Size}, tags: {tags.Gen2.Text}");
        logger.LogDebug($"clr_diagnostics_timer.gc.gc_size: {statistics.LohSize}, tags: {tags.Loh.Text}");
        logger.LogDebug($"clr_diagnostics_timer.gc.time_in_gc_percent: {statistics.TimeInGc}, tags: {tags.Base.Text}");
    }

    // Process
    public static void ProcessInfoTimerGauge(in ProcessInfoStatistics statistics, ILogger logger)
    {
        logger.LogDebug($"clr_diagnostics_timer.process.cpu: {statistics.Cpu}, tags: ");
        logger.LogDebug($"clr_diagnostics_timer.process.private_bytes: {statistics.PrivateBytes}, tags: ");
        logger.LogDebug($"clr_diagnostics_timer.process.working_sets: {statistics.WorkingSet}, tags: ");
    }

    // Thread
    public static void ThreadInfoTimerGauge(in ThreadInfoStatistics statistics, ILogger logger)
    {
        logger.LogDebug($"clr_diagnostics_timer.thread.available_worker_threads: {statistics.AvailableWorkerThreads}, tags: ");
        logger.LogDebug($"clr_diagnostics_timer.thread.available_completion_port_threads: {statistics.AvailableCompletionPortThreads}, tags: ");
        logger.LogDebug($"clr_diagnostics_timer.thread.max_worker_threads: {statistics.MaxWorkerThreads}, tags: ");
        logger.LogDebug($"clr_diagnostics_timer.thread.max_completion_port_threads: {statistics.MaxCompletionPortThreads}, tags: ");
        logger.LogDebug($"clr_diagnostics_timer.thread.using_worker_threads: {statistics.UsingWorkerThreads}, tags: ");
        logger.LogDebug($"clr_diagnostics_timer.thread.using_completion_port_threads: {statistics.UsingCompletionPortThreads}, tags: ");
        logger.LogDebug($"clr_diagnostics_timer.thread.thread_count: {statistics.ThreadCount}, tags: ");
        logger.LogDebug($"clr_diagnostics_timer.thread.queue_length: {statistics.QueueLength}, tags: ");
        logger.LogDebug($"clr_diagnostics_timer.thread.lock_contention_count: {statistics.LockContentionCount}, tags: ");
        logger.LogDebug($"clr_diagnostics_timer.thread.completed_items_count: {statistics.CompletedItemsCount}, tags: ");
    }
}
