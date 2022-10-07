using ClrProfiler.Statistics;
using Microsoft.Extensions.Logging;

namespace ClrProfiler.DatadogTracing;

public class ClrTracker
{
    private static int singleAccess = 0;
    private readonly ILogger _logger;

    public ClrTracker(ILogger logger)
    {
        _logger = logger;
        EnableTracker();
    }

    private void EnableTracker()
    {
        // Single access guard
        Interlocked.Increment(ref singleAccess);
        if (singleAccess != 1) return;

        _logger.LogInformation($"Enable tracking {nameof(ClrTracker)}");

        // InProcess tracker
        ProfilerTracker.Options = new ProfilerTrackerOptions
        {
            ContentionEventCallback = (ContentionEventProfilerCallbackAsync, OnException),
            GCEventCallback = (GCEventProfilerCallbackAsync, OnException),
            ThreadPoolEventCallback = (ThreadPoolEventProfilerCallbackAsync, OnException),
            GCInfoTimerCallback = (GCInfoTimerCallbackAsync, OnException),
            ProcessInfoTimerCallback = (ProcessInfoTimerCallbackAsync, OnException),
            ThreadInfoTimerCallback = (ThreadInfoTimerCallbackAsync, OnException),
        };
    }
    public void StartTracker()
    {
        _logger.LogDebug($"Start tracking {nameof(ClrTracker)}");
        ProfilerTracker.Current.Value.Start();
    }
    public void StopTracker()
    {
        _logger.LogDebug($"Stop tracking {nameof(ClrTracker)}");
        ProfilerTracker.Current.Value.Stop();
    }
    public void CancelTracker()
    {
        _logger.LogDebug($"Cancel tracking {nameof(ClrTracker)}");
        ProfilerTracker.Current.Value.Cancel();
    }
    private void OnException(Exception exception) => _logger.LogCritical(exception, exception.Message);

    /// <summary>
    /// Contention Event
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task ContentionEventProfilerCallbackAsync(ContentionEventStatistics arg)
    {
        DatadogTracing.ContentionEventStartEnd(arg);
        return Task.CompletedTask;
    }

    /// <summary>
    /// GC Event
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task GCEventProfilerCallbackAsync(GCEventStatistics arg)
    {
        if (arg.Type == GCEventType.GCStartEnd)
        {
            DatadogTracing.GcEventStartEnd(arg.GCStartEndStatistics);
        }
        else if (arg.Type == GCEventType.GCSuspend)
        {
            DatadogTracing.GcEventSuspend(arg.GCSuspendStatistics);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// ThreadPool Event
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task ThreadPoolEventProfilerCallbackAsync(ThreadPoolEventStatistics arg)
    {
        if (arg.Type == ThreadPoolStatisticType.ThreadPoolWorkerStartStop)
        {
            DatadogTracing.ThreadPoolEventWorker(arg.ThreadPoolWorker);
        }
        else if (arg.Type == ThreadPoolStatisticType.ThreadPoolAdjustment)
        {
            DatadogTracing.ThreadPoolEventAdjustment(arg.ThreadPoolAdjustment);

            if (arg.ThreadPoolAdjustment.Reason == 0x06)
            {
                // special handling for threadPool starvation. This is really critical for .NET (.NET Core) Apps.
                DatadogTracing.ThreadPoolStarvationEventAdjustment(arg.ThreadPoolAdjustment);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// GCInfo
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task GCInfoTimerCallbackAsync(GCInfoStatistics arg)
    {
        DatadogTracing.GcInfoTimerGauge(arg);
        return Task.CompletedTask;
    }

    /// <summary>
    /// ProcessInfo
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task ProcessInfoTimerCallbackAsync(ProcessInfoStatistics arg)
    {
        DatadogTracing.ProcessInfoTimerGauge(arg);
        return Task.CompletedTask;
    }

    /// <summary>
    /// ThreadInfo
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task ThreadInfoTimerCallbackAsync(ThreadInfoStatistics arg)
    {
        DatadogTracing.ThreadInfoTimerGauge(arg);
        var usingWorkerThreads = arg.MaxWorkerThreads - arg.AvailableWorkerThreads;
        return Task.CompletedTask;
    }
}
