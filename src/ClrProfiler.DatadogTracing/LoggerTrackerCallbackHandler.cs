using ClrProfiler.Statistics;
using Microsoft.Extensions.Logging;

namespace ClrProfiler.DatadogTracing;

public class LoggerTrackerCallbackHandler(ILogger logger) : IClrTrackerCallbackHandler
{
    public async Task OnContentionEventAsync(ContentionEventStatistics statistics)
    {
        LoggerTracing.ContentionEventStartEnd(statistics, logger);
    }

    public Task OnGCEventAsync(GCEventStatistics statistics)
    {
        if (statistics.Type == GCEventType.GCStartEnd)
        {
            LoggerTracing.GcEventStartEnd(statistics.GCStartEndStatistics, logger);
        }
        else if (statistics.Type == GCEventType.GCSuspend)
        {
            LoggerTracing.GcEventSuspend(statistics.GCSuspendStatistics, logger);
        }
        return Task.CompletedTask;
    }

    public Task OnGCInfoTimerAsync(GCInfoStatistics statistics)
    {
        LoggerTracing.GcInfoTimerGauge(statistics, logger);
        return Task.CompletedTask;
    }

    public Task OnProcessInfoTimerAsync(ProcessInfoStatistics statistics)
    {
        LoggerTracing.ProcessInfoTimerGauge(statistics, logger);
        return Task.CompletedTask;
    }

    public Task OnThreadInfoTimerAsync(ThreadInfoStatistics statistics)
    {
        LoggerTracing.ThreadInfoTimerGauge(statistics, logger);
        //var usingWorkerThreads = statistics.MaxWorkerThreads - statistics.AvailableWorkerThreads;
        return Task.CompletedTask;
    }

    public Task OnThreadPoolEventAsync(ThreadPoolEventStatistics statistics)
    {
        if (statistics.Type == ThreadPoolStatisticType.ThreadPoolWorkerStartStop)
        {
            LoggerTracing.ThreadPoolEventWorker(statistics.ThreadPoolWorker, logger);
        }
        else if (statistics.Type == ThreadPoolStatisticType.ThreadPoolAdjustment)
        {
            LoggerTracing.ThreadPoolEventAdjustment(statistics.ThreadPoolAdjustment, logger);

            if (statistics.ThreadPoolAdjustment.Reason == 0x06)
            {
                // special handling for threadPool starvation. This is really critical for .NET (.NET Core) Apps.
                LoggerTracing.ThreadPoolStarvationEventAdjustment(statistics.ThreadPoolAdjustment, logger);
            }
        }
        return Task.CompletedTask;
    }

    public void OnException(Exception exception)
    {
        logger.LogCritical(exception, exception.Message);
    }
}
