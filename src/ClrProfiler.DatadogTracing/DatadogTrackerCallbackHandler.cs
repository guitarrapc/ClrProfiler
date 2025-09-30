using ClrProfiler.Statistics;
using Microsoft.Extensions.Logging;

namespace ClrProfiler.DatadogTracing;

public class DatadogTrackerCallbackHandler(ILogger logger) : IClrTrackerCallbackHandler
{
    public Task OnContentionEventAsync(ContentionEventStatistics statistics)
    {
        DatadogTracing.ContentionEventStartEnd(statistics);
        return Task.CompletedTask;
    }

    public Task OnGCEventAsync(GCEventStatistics statistics)
    {
        if (statistics.Type == GCEventType.GCStartEnd)
        {
            DatadogTracing.GcEventStartEnd(statistics.GCStartEndStatistics);
        }
        else if (statistics.Type == GCEventType.GCSuspend)
        {
            DatadogTracing.GcEventSuspend(statistics.GCSuspendStatistics);
        }
        return Task.CompletedTask;
    }

    public Task OnGCInfoTimerAsync(GCInfoStatistics statistics)
    {
        DatadogTracing.GcInfoTimerGauge(statistics);
        return Task.CompletedTask;
    }

    public Task OnProcessInfoTimerAsync(ProcessInfoStatistics statistics)
    {
        DatadogTracing.ProcessInfoTimerGauge(statistics);
        return Task.CompletedTask;
    }

    public Task OnThreadInfoTimerAsync(ThreadInfoStatistics statistics)
    {
        DatadogTracing.ThreadInfoTimerGauge(statistics);
        //var usingWorkerThreads = statistics.MaxWorkerThreads - statistics.AvailableWorkerThreads;
        return Task.CompletedTask;
    }

    public Task OnThreadPoolEventAsync(ThreadPoolEventStatistics statistics)
    {
        if (statistics.Type == ThreadPoolStatisticType.ThreadPoolWorkerStartStop)
        {
            DatadogTracing.ThreadPoolEventWorker(statistics.ThreadPoolWorker);
        }
        else if (statistics.Type == ThreadPoolStatisticType.ThreadPoolAdjustment)
        {
            DatadogTracing.ThreadPoolEventAdjustment(statistics.ThreadPoolAdjustment);

            if (statistics.ThreadPoolAdjustment.Reason == 0x06)
            {
                // special handling for threadPool starvation. This is really critical for .NET (.NET Core) Apps.
                DatadogTracing.ThreadPoolStarvationEventAdjustment(statistics.ThreadPoolAdjustment);
            }
        }
        return Task.CompletedTask;
    }

    public void OnException(Exception exception)
    {
        logger.LogCritical(exception, exception.Message);
    }
}
