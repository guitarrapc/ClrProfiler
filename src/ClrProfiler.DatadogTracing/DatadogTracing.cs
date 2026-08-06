using ClrProfiler.Statistics;
using StatsdClient;

namespace ClrProfiler.DatadogTracing;

public static class DatadogTracing
{
    // ContentionEvent
    public static void ContentionEventStartEnd(in ContentionEventStatistics statistics)
    {
        ref readonly var tags = ref MetricTags.GetContention(statistics.Flag);
        DogStatsd.Counter(MetricNames.Event.ContentionStartEndCount, statistics.Count, tags: tags.Values);
        DogStatsd.Gauge(MetricNames.Event.ContentionStartEndDurationNs, statistics.DurationNsMax, tags: tags.Values);
        DogStatsd.Counter(MetricNames.Event.ContentionStartEndDurationNsSum, statistics.DurationNsSum, tags: tags.Values);
    }

    // GCEvent
    public static void GcEventStartEnd(in GCStartEndStatistics statistics)
    {
        ref readonly var tags = ref MetricTags.GetGcStartEnd(statistics.Generation, statistics.Type, statistics.Reason);
        DogStatsd.Increment(MetricNames.Event.GcStartEndCount, tags: tags.Values);
        DogStatsd.Gauge(MetricNames.Event.GcStartEndDurationMs, statistics.DurationMillsec, tags: tags.Values);
    }

    public static void GcEventSuspend(in GCSuspendStatistics statistics)
    {
        ref readonly var tags = ref MetricTags.GetGcSuspend(statistics.Reason);
        DogStatsd.Counter(MetricNames.Event.GcSuspendObjectCount, statistics.Count, tags: tags.Values);
        DogStatsd.Gauge(MetricNames.Event.GcSuspendDurationMs, statistics.DurationMillisec, tags: tags.Values);
    }

    // ThreadPoolEvent
    public static void ThreadPoolEventWorker(in ThreadPoolWorkerStatistics statistics)
    {
        DogStatsd.Gauge(MetricNames.Event.ThreadPoolAvailableWorkerThreadCount, statistics.ActiveWrokerThreads);
    }
    public static void ThreadPoolEventAdjustment(in ThreadPoolAdjustmentStatistics statistics)
    {
        ref readonly var tags = ref MetricTags.GetThreadAdjustment(statistics.Reason);
        DogStatsd.Gauge(MetricNames.Event.ThreadPoolAdjustmentAverageThroughput, statistics.AverageThrouput, tags: tags.Values);
        DogStatsd.Gauge(MetricNames.Event.ThreadPoolAdjustmentNewWorkerThreadsCount, statistics.NewWorkerThreads, tags: tags.Values);
    }
    public static void ThreadPoolStarvationEventAdjustment(in ThreadPoolAdjustmentStatistics statistics)
    {
        DogStatsd.Event("ThreadPool Starvation detected", ".NET CLR automatically expanding ThreadPool, but this results slow down system. Watch out for error increase and take action to expan thread pool in advance.", alertType: "warning", aggregationKey: "host");
    }

    // GC
    public static void GcInfoTimerGauge(in GCInfoStatistics statistics)
    {
        ref readonly var tags = ref MetricTags.GetGcInfo(statistics.GCMode, statistics.LatencyMode, statistics.CompactionMode);

        DogStatsd.Gauge(MetricNames.Timer.GcHeapSizeBytes, statistics.HeapSize, tags: tags.Base.Values);
        DogStatsd.Gauge(MetricNames.Timer.GcTotalAllocationBytes, statistics.TotalAllocationBytes, tags: tags.Base.Values);
        DogStatsd.Gauge(MetricNames.Timer.GcCount, statistics.Gen0Count, tags: tags.Gen0.Values);
        DogStatsd.Gauge(MetricNames.Timer.GcCount, statistics.Gen1Count, tags: tags.Gen1.Values);
        DogStatsd.Gauge(MetricNames.Timer.GcCount, statistics.Gen2Count, tags: tags.Gen2.Values);
        DogStatsd.Gauge(MetricNames.Timer.GcSize, statistics.Gen0Size, tags: tags.Gen0.Values);
        DogStatsd.Gauge(MetricNames.Timer.GcSize, statistics.Gen1Size, tags: tags.Gen1.Values);
        DogStatsd.Gauge(MetricNames.Timer.GcSize, statistics.Gen2Size, tags: tags.Gen2.Values);
        DogStatsd.Gauge(MetricNames.Timer.GcSize, statistics.LohSize, tags: tags.Loh.Values);
        DogStatsd.Gauge(MetricNames.Timer.GcTimeInGcPercent, statistics.TimeInGc, tags: tags.Base.Values);
    }

    // Process
    public static void ProcessInfoTimerGauge(in ProcessInfoStatistics statistics)
    {
        DogStatsd.Gauge(MetricNames.Timer.ProcessCpu, statistics.Cpu);
        DogStatsd.Gauge(MetricNames.Timer.ProcessPrivateBytes, statistics.PrivateBytes);
        DogStatsd.Gauge(MetricNames.Timer.ProcessWorkingSets, statistics.WorkingSet);
    }

    // Thread
    public static void ThreadInfoTimerGauge(in ThreadInfoStatistics statistics)
    {
        DogStatsd.Gauge(MetricNames.Timer.ThreadAvailableWorkerThreads, statistics.AvailableWorkerThreads);
        DogStatsd.Gauge(MetricNames.Timer.ThreadAvailableCompletionPortThreads, statistics.AvailableCompletionPortThreads);
        DogStatsd.Gauge(MetricNames.Timer.ThreadMaxWorkerThreads, statistics.MaxWorkerThreads);
        DogStatsd.Gauge(MetricNames.Timer.ThreadMaxCompletionPortThreads, statistics.MaxCompletionPortThreads);
        DogStatsd.Gauge(MetricNames.Timer.ThreadUsingWorkerThreads, statistics.UsingWorkerThreads);
        DogStatsd.Gauge(MetricNames.Timer.ThreadUsingCompletionPortThreads, statistics.UsingCompletionPortThreads);
        DogStatsd.Gauge(MetricNames.Timer.ThreadCount, statistics.ThreadCount);
        DogStatsd.Gauge(MetricNames.Timer.ThreadQueueLength, statistics.QueueLength);
        DogStatsd.Gauge(MetricNames.Timer.ThreadLockContentionCount, statistics.LockContentionCount);
        DogStatsd.Gauge(MetricNames.Timer.ThreadCompletedItemsCount, statistics.CompletedItemsCount);
    }
}
