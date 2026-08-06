namespace ClrProfiler.DatadogTracing;

/// <summary>
/// Canonical names and tag contracts for every metric emitted by the Datadog and logger adapters.
/// </summary>
/// <remarks>
/// Metric names are constants so using this catalog adds no lookup or allocation to metric hot paths.
/// Unknown runtime tag values are projected to <c>unknown</c> by <see cref="MetricTags"/>.
/// </remarks>
internal static class MetricNames
{
    /// <summary>Metrics produced from CLR events.</summary>
    /// <remarks>
    /// Tag values:
    /// <list type="bullet">
    /// <item><c>contention_type:0|1|unknown</c></item>
    /// <item><c>gc_gen:0|1|2|unknown</c></item>
    /// <item><c>gc_type:0|1|2|unknown</c></item>
    /// <item><c>gc_reason:soh|induced|low_memory|empty|loh|oos_soh|oos_loh|incuded_non_forceblock|stress_testing|finalizer_low_memory_induced|user_gc_request|unknown</c></item>
    /// <item><c>gc_suspend_reason:other|gc|appdomain_shudown|code_pitch|shutdown|debugger|prep_gc|unknown</c></item>
    /// <item><c>thread_adjust_reason:warmup|initializing|random_move|climbing_move|change_point|stabilizing|starvation|timedout|cooperative_blocking|unknown</c></item>
    /// </list>
    /// </remarks>
    internal static class Event
    {
        /// <summary>Counter incremented by the aggregated contention count. Tags: <c>contention_type:0|1|unknown</c>.</summary>
        internal const string ContentionStartEndCount = "clr_diagnostics_event.contention.startend_count";

        /// <summary>Counter incremented by the total contention duration in nanoseconds. Divide by <see cref="ContentionStartEndCount"/> for the mean duration. Tags: <c>contention_type:0|1|unknown</c>.</summary>
        internal const string ContentionStartEndDurationNsSum = "clr_diagnostics_event.contention.startend_duration_ns_sum";

        /// <summary>
        /// Histogram of the longest contention duration in nanoseconds per aggregation window. Read
        /// the <c>.max</c> series: the maximum of the submitted window maxima is the true maximum for
        /// the flush interval. The <c>.avg</c>, <c>.count</c>, and percentile series describe window
        /// maxima rather than individual contentions, so they are not meaningful as durations.
        /// Tags: <c>contention_type:0|1|unknown</c>.
        /// </summary>
        internal const string ContentionStartEndDurationNsMax = "clr_diagnostics_event.contention.startend_duration_ns_max";

        /// <summary>Counter. Tags: <c>gc_gen</c>, <c>gc_type</c>, and <c>gc_reason</c>.</summary>
        internal const string GcStartEndCount = "clr_diagnostics_event.gc.startend_count";

        /// <summary>Gauge in milliseconds. Tags: <c>gc_gen</c>, <c>gc_type</c>, and <c>gc_reason</c>.</summary>
        internal const string GcStartEndDurationMs = "clr_diagnostics_event.gc.startend_duration_ms";

        /// <summary>Counter. Tags: <c>gc_suspend_reason</c>.</summary>
        internal const string GcSuspendObjectCount = "clr_diagnostics_event.gc.suspend_object_count";

        /// <summary>Gauge in milliseconds. Tags: <c>gc_suspend_reason</c>.</summary>
        internal const string GcSuspendDurationMs = "clr_diagnostics_event.gc.suspend_duration_ms";

        /// <summary>Gauge without metric-specific tags.</summary>
        internal const string ThreadPoolAvailableWorkerThreadCount = "clr_diagnostics_event.threadpool.available_workerthread_count";

        /// <summary>Gauge. Tags: <c>thread_adjust_reason</c>.</summary>
        internal const string ThreadPoolAdjustmentAverageThroughput = "clr_diagnostics_event.threadpool.adjustment_avg_throughput";

        /// <summary>Gauge. Tags: <c>thread_adjust_reason</c>.</summary>
        internal const string ThreadPoolAdjustmentNewWorkerThreadsCount = "clr_diagnostics_event.threadpool.adjustment_new_workerthreads_count";

        private static readonly string[] Values =
        [
            ContentionStartEndCount,
            ContentionStartEndDurationNsSum,
            ContentionStartEndDurationNsMax,
            GcStartEndCount,
            GcStartEndDurationMs,
            GcSuspendObjectCount,
            GcSuspendDurationMs,
            ThreadPoolAvailableWorkerThreadCount,
            ThreadPoolAdjustmentAverageThroughput,
            ThreadPoolAdjustmentNewWorkerThreadsCount,
        ];

        /// <summary>All CLR event metric names in catalog order.</summary>
        internal static ReadOnlySpan<string> All => Values;
    }

    /// <summary>Metrics produced by periodic runtime sampling.</summary>
    /// <remarks>
    /// GC timer tag values:
    /// <list type="bullet">
    /// <item><c>gc_gen:0|1|2|loh</c> on generation-specific metrics</item>
    /// <item><c>gc_mode:Workstation|Server|unknown</c></item>
    /// <item><c>latency_mode:Batch|Interactive|LowLatency|SustainedLowLatency|NoGCRegion|unknown</c></item>
    /// <item><c>compaction_mode:Default|CompactOnce|unknown</c></item>
    /// </list>
    /// Process and thread timer metrics have no metric-specific tags.
    /// </remarks>
    internal static class Timer
    {
        /// <summary>Gauge in bytes. Tags: <c>gc_mode</c>, <c>latency_mode</c>, and <c>compaction_mode</c>.</summary>
        internal const string GcHeapSizeBytes = "clr_diagnostics_timer.gc.heap_size_bytes";

        /// <summary>Gauge in bytes. Tags: <c>gc_mode</c>, <c>latency_mode</c>, and <c>compaction_mode</c>.</summary>
        internal const string GcTotalAllocationBytes = "clr_diagnostics_timer.gc.total_allocation_bytes";

        /// <summary>Gauge. Tags: <c>gc_gen:0|1|2</c> plus the GC mode tags.</summary>
        internal const string GcCount = "clr_diagnostics_timer.gc.gc_count";

        /// <summary>Gauge in bytes. Tags: <c>gc_gen:0|1|2|loh</c> plus the GC mode tags.</summary>
        internal const string GcSize = "clr_diagnostics_timer.gc.gc_size";

        /// <summary>Gauge as a percentage. Tags: <c>gc_mode</c>, <c>latency_mode</c>, and <c>compaction_mode</c>.</summary>
        internal const string GcTimeInGcPercent = "clr_diagnostics_timer.gc.time_in_gc_percent";

        /// <summary>Gauge without metric-specific tags.</summary>
        internal const string ProcessCpu = "clr_diagnostics_timer.process.cpu";

        /// <summary>Gauge in bytes without metric-specific tags.</summary>
        internal const string ProcessPrivateBytes = "clr_diagnostics_timer.process.private_bytes";

        /// <summary>Gauge in bytes without metric-specific tags.</summary>
        internal const string ProcessWorkingSets = "clr_diagnostics_timer.process.working_sets";

        /// <summary>Gauge without metric-specific tags.</summary>
        internal const string ThreadAvailableWorkerThreads = "clr_diagnostics_timer.thread.available_worker_threads";

        /// <summary>Gauge without metric-specific tags.</summary>
        internal const string ThreadAvailableCompletionPortThreads = "clr_diagnostics_timer.thread.available_completion_port_threads";

        /// <summary>Gauge without metric-specific tags.</summary>
        internal const string ThreadMaxWorkerThreads = "clr_diagnostics_timer.thread.max_worker_threads";

        /// <summary>Gauge without metric-specific tags.</summary>
        internal const string ThreadMaxCompletionPortThreads = "clr_diagnostics_timer.thread.max_completion_port_threads";

        /// <summary>Gauge without metric-specific tags.</summary>
        internal const string ThreadUsingWorkerThreads = "clr_diagnostics_timer.thread.using_worker_threads";

        /// <summary>Gauge without metric-specific tags.</summary>
        internal const string ThreadUsingCompletionPortThreads = "clr_diagnostics_timer.thread.using_completion_port_threads";

        /// <summary>Gauge without metric-specific tags.</summary>
        internal const string ThreadCount = "clr_diagnostics_timer.thread.thread_count";

        /// <summary>Gauge without metric-specific tags.</summary>
        internal const string ThreadQueueLength = "clr_diagnostics_timer.thread.queue_length";

        /// <summary>Gauge without metric-specific tags.</summary>
        internal const string ThreadLockContentionCount = "clr_diagnostics_timer.thread.lock_contention_count";

        /// <summary>Gauge without metric-specific tags.</summary>
        internal const string ThreadCompletedItemsCount = "clr_diagnostics_timer.thread.completed_items_count";

        private static readonly string[] Values =
        [
            GcHeapSizeBytes,
            GcTotalAllocationBytes,
            GcCount,
            GcSize,
            GcTimeInGcPercent,
            ProcessCpu,
            ProcessPrivateBytes,
            ProcessWorkingSets,
            ThreadAvailableWorkerThreads,
            ThreadAvailableCompletionPortThreads,
            ThreadMaxWorkerThreads,
            ThreadMaxCompletionPortThreads,
            ThreadUsingWorkerThreads,
            ThreadUsingCompletionPortThreads,
            ThreadCount,
            ThreadQueueLength,
            ThreadLockContentionCount,
            ThreadCompletedItemsCount,
        ];

        /// <summary>All timer metric names in catalog order.</summary>
        internal static ReadOnlySpan<string> All => Values;
    }
}
