using ClrProfiler.Statistics;
using Microsoft.Extensions.Logging;

namespace ClrProfiler.DatadogTracing;

public class ClrTracker
{
    private static int singleAccess = 0;
    private readonly ILogger<ClrTracker> _logger;
    private readonly ClrTrackerOptions _options;
    private bool _enabled;

    public ClrTrackerType TrackerType => _options.TrackerType;

    public ClrTracker(ILoggerFactory loggerFactory) : this(loggerFactory, ClrTrackerOptions.Default)
    {
    }

    public ClrTracker(ILoggerFactory loggerFactory, ClrTrackerOptions options)
    {
        _logger = loggerFactory.CreateLogger<ClrTracker>();
        _options = options;
    }

    public void EnableTracker()
    {
        // Single access guard
        Interlocked.Increment(ref singleAccess);
        if (singleAccess != 1) return;

        _logger.LogDebug($"Enable {nameof(ClrTracker)}");
        _enabled = true;

        // InProcess tracker
        switch (_options.TrackerType)
        {
            case ClrTrackerType.Datadog:
                ProfilerTracker.Options = new ProfilerTrackerOptions
                {
                    ContentionEventCallback = (DatadogContentionEventProfilerCallbackAsync, OnException),
                    GCEventCallback = (DatadogGCEventProfilerCallbackAsync, OnException),
                    ThreadPoolEventCallback = (DatadogThreadPoolEventProfilerCallbackAsync, OnException),
                    GCInfoTimerCallback = (DatadogGCInfoTimerCallbackAsync, OnException),
                    ProcessInfoTimerCallback = (DatadogProcessInfoTimerCallbackAsync, OnException),
                    ThreadInfoTimerCallback = (DatadogThreadInfoTimerCallbackAsync, OnException),
                };
                break;
            case ClrTrackerType.Logger:
                ProfilerTracker.Options = new ProfilerTrackerOptions
                {
                    ContentionEventCallback = (LoggerContentionEventProfilerCallbackAsync, OnException),
                    GCEventCallback = (LoggerGCEventProfilerCallbackAsync, OnException),
                    ThreadPoolEventCallback = (LoggerThreadPoolEventProfilerCallbackAsync, OnException),
                    GCInfoTimerCallback = (LoggerGCInfoTimerCallbackAsync, OnException),
                    ProcessInfoTimerCallback = (LoggerProcessInfoTimerCallbackAsync, OnException),
                    ThreadInfoTimerCallback = (LoggerThreadInfoTimerCallbackAsync, OnException),
                };
                break;
            default:
                throw new NotImplementedException($"{nameof(ClrTrackerType)}: {_options.TrackerType} not implemeted.");
        }
    }
    public void StartTracker()
    {
        if (!_enabled) return;
        _logger.LogDebug($"Start tracking {nameof(ClrTracker)}");
        ProfilerTracker.Current.Value.Start();
    }
    public void StopTracker()
    {
        if (!_enabled) return;
        _logger.LogDebug($"Stop tracking {nameof(ClrTracker)}");
        ProfilerTracker.Current.Value.Stop();
    }
    public void CancelTracker()
    {
        if (!_enabled) return;
        _logger.LogDebug($"Cancel tracking {nameof(ClrTracker)}");
        ProfilerTracker.Current.Value.Cancel();
    }
    private void OnException(Exception exception) => _logger.LogCritical(exception, exception.Message);

    // Datadog

    /// <summary>
    /// Contention Event
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task DatadogContentionEventProfilerCallbackAsync(ContentionEventStatistics arg)
    {
        DatadogTracing.ContentionEventStartEnd(arg);
        return Task.CompletedTask;
    }

    /// <summary>
    /// GC Event
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task DatadogGCEventProfilerCallbackAsync(GCEventStatistics arg)
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
    private Task DatadogThreadPoolEventProfilerCallbackAsync(ThreadPoolEventStatistics arg)
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
    private Task DatadogGCInfoTimerCallbackAsync(GCInfoStatistics arg)
    {
        DatadogTracing.GcInfoTimerGauge(arg);
        return Task.CompletedTask;
    }

    /// <summary>
    /// ProcessInfo
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task DatadogProcessInfoTimerCallbackAsync(ProcessInfoStatistics arg)
    {
        DatadogTracing.ProcessInfoTimerGauge(arg);
        return Task.CompletedTask;
    }

    /// <summary>
    /// ThreadInfo
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task DatadogThreadInfoTimerCallbackAsync(ThreadInfoStatistics arg)
    {
        DatadogTracing.ThreadInfoTimerGauge(arg);
        var usingWorkerThreads = arg.MaxWorkerThreads - arg.AvailableWorkerThreads;
        return Task.CompletedTask;
    }

    // Logger

    /// <summary>
    /// Contention Event
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task LoggerContentionEventProfilerCallbackAsync(ContentionEventStatistics arg)
    {
        LoggerTracing.ContentionEventStartEnd(arg, _logger);
        return Task.CompletedTask;
    }

    /// <summary>
    /// GC Event
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task LoggerGCEventProfilerCallbackAsync(GCEventStatistics arg)
    {
        if (arg.Type == GCEventType.GCStartEnd)
        {
            LoggerTracing.GcEventStartEnd(arg.GCStartEndStatistics, _logger);
        }
        else if (arg.Type == GCEventType.GCSuspend)
        {
            LoggerTracing.GcEventSuspend(arg.GCSuspendStatistics, _logger);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// ThreadPool Event
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task LoggerThreadPoolEventProfilerCallbackAsync(ThreadPoolEventStatistics arg)
    {
        if (arg.Type == ThreadPoolStatisticType.ThreadPoolWorkerStartStop)
        {
            LoggerTracing.ThreadPoolEventWorker(arg.ThreadPoolWorker, _logger);
        }
        else if (arg.Type == ThreadPoolStatisticType.ThreadPoolAdjustment)
        {
            LoggerTracing.ThreadPoolEventAdjustment(arg.ThreadPoolAdjustment, _logger);

            if (arg.ThreadPoolAdjustment.Reason == 0x06)
            {
                // special handling for threadPool starvation. This is really critical for .NET (.NET Core) Apps.
                LoggerTracing.ThreadPoolStarvationEventAdjustment(arg.ThreadPoolAdjustment, _logger);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// GCInfo
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task LoggerGCInfoTimerCallbackAsync(GCInfoStatistics arg)
    {
        LoggerTracing.GcInfoTimerGauge(arg, _logger);
        return Task.CompletedTask;
    }

    /// <summary>
    /// ProcessInfo
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task LoggerProcessInfoTimerCallbackAsync(ProcessInfoStatistics arg)
    {
        LoggerTracing.ProcessInfoTimerGauge(arg, _logger);
        return Task.CompletedTask;
    }

    /// <summary>
    /// ThreadInfo
    /// </summary>
    /// <param name="arg"></param>
    /// <returns></returns>
    private Task LoggerThreadInfoTimerCallbackAsync(ThreadInfoStatistics arg)
    {
        LoggerTracing.ThreadInfoTimerGauge(arg, _logger);
        var usingWorkerThreads = arg.MaxWorkerThreads - arg.AvailableWorkerThreads;
        return Task.CompletedTask;
    }
}
