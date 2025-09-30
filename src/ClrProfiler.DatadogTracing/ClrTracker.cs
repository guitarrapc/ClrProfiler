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
        ProfilerTracker.Options = _options.TrackerType switch
        {
            ClrTrackerType.Datadog => RegisterDatadogProfilerTrackerOptions(),
            ClrTrackerType.Logger => RegisterLoggerProfilerTrackerOptions(),
            ClrTrackerType.Custom when _options.CustomHandler is not null => ProfilerTracker.Options = new ProfilerTrackerOptions
            {
                ContentionEventCallback = (_options.CustomHandler.OnContentionEventAsync, _options.CustomHandler.OnException),
                GCEventCallback = (_options.CustomHandler.OnGCEventAsync, _options.CustomHandler.OnException),
                ThreadPoolEventCallback = (_options.CustomHandler.OnThreadPoolEventAsync, _options.CustomHandler.OnException),
                GCInfoTimerCallback = (_options.CustomHandler.OnGCInfoTimerAsync, _options.CustomHandler.OnException),
                ProcessInfoTimerCallback = (_options.CustomHandler.OnProcessInfoTimerAsync, _options.CustomHandler.OnException),
                ThreadInfoTimerCallback = (_options.CustomHandler.OnThreadInfoTimerAsync, _options.CustomHandler.OnException),
            },
            ClrTrackerType.Custom when _options.CustomHandler is null => throw new ArgumentException($"{nameof(ClrTrackerType.Custom)}: {_options.CustomHandler} is null, you must set custom Handler."),
            _ => throw new NotImplementedException($"{nameof(ClrTrackerType)}: {_options.TrackerType} not implemeted."),
        };
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

    private ProfilerTrackerOptions RegisterDatadogProfilerTrackerOptions()
    {
        var datadogTrackerHandler = new DatadogTrackerCallbackHandler(_logger);
        return new ProfilerTrackerOptions
        {
            ContentionEventCallback = (datadogTrackerHandler.OnContentionEventAsync, datadogTrackerHandler.OnException),
            GCEventCallback = (datadogTrackerHandler.OnGCEventAsync, datadogTrackerHandler.OnException),
            ThreadPoolEventCallback = (datadogTrackerHandler.OnThreadPoolEventAsync, datadogTrackerHandler.OnException),
            GCInfoTimerCallback = (datadogTrackerHandler.OnGCInfoTimerAsync, datadogTrackerHandler.OnException),
            ProcessInfoTimerCallback = (datadogTrackerHandler.OnProcessInfoTimerAsync, datadogTrackerHandler.OnException),
            ThreadInfoTimerCallback = (datadogTrackerHandler.OnThreadInfoTimerAsync, datadogTrackerHandler.OnException),
        };
    }

    private ProfilerTrackerOptions RegisterLoggerProfilerTrackerOptions()
    {
        var loggerTrackerHandler = new LoggerTrackerCallbackHandler(_logger);
        return new ProfilerTrackerOptions
        {
            ContentionEventCallback = (loggerTrackerHandler.OnContentionEventAsync, loggerTrackerHandler.OnException),
            GCEventCallback = (loggerTrackerHandler.OnGCEventAsync, loggerTrackerHandler.OnException),
            ThreadPoolEventCallback = (loggerTrackerHandler.OnThreadPoolEventAsync, loggerTrackerHandler.OnException),
            GCInfoTimerCallback = (loggerTrackerHandler.OnGCInfoTimerAsync, loggerTrackerHandler.OnException),
            ProcessInfoTimerCallback = (loggerTrackerHandler.OnProcessInfoTimerAsync, loggerTrackerHandler.OnException),
            ThreadInfoTimerCallback = (loggerTrackerHandler.OnThreadInfoTimerAsync, loggerTrackerHandler.OnException),
        };
    }
}
