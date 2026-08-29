using Microsoft.Extensions.Logging;

namespace ClrProfiler.DatadogTracing;

public class ClrTracker : IDisposable
{
    private readonly ILogger<ClrTracker> _logger;
    private readonly ClrTrackerOptions _options;
    private readonly Action _initializeMetricTags;
    private readonly object _lifecycleLock = new();
    private ProfilerTracker? _profilerTracker;
    private bool _enabled;
    private bool _disposed;

    public ClrTrackerType TrackerType => _options.TrackerType;

    public ClrTracker(ILoggerFactory loggerFactory) : this(loggerFactory, ClrTrackerOptions.Default)
    {
    }

    public ClrTracker(ILoggerFactory loggerFactory, ClrTrackerOptions options)
        : this(loggerFactory, options, MetricTags.Initialize)
    {
    }

    internal ClrTracker(ILoggerFactory loggerFactory, ClrTrackerOptions options, Action initializeMetricTags)
    {
        _logger = loggerFactory.CreateLogger<ClrTracker>();
        _options = options;
        _initializeMetricTags = initializeMetricTags;
    }

    public void EnableTracker()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_enabled) return;

            LogMessages.EnableTracker(_logger);

            if (_options.TrackerType is ClrTrackerType.Datadog or ClrTrackerType.Logger)
            {
                _initializeMetricTags();
            }

            var profilerOptions = _options.TrackerType switch
            {
                ClrTrackerType.Datadog => RegisterDatadogProfilerTrackerOptions(),
                ClrTrackerType.Logger => RegisterLoggerProfilerTrackerOptions(),
                ClrTrackerType.Custom when _options.CustomHandler is not null => new ProfilerTrackerOptions
                {
                    ContentionEventCallback = (_options.CustomHandler.OnContentionEventAsync, _options.CustomHandler.OnException),
                    GCEventCallback = (_options.CustomHandler.OnGCEventAsync, _options.CustomHandler.OnException),
                    ThreadPoolEventCallback = (_options.CustomHandler.OnThreadPoolEventAsync, _options.CustomHandler.OnException),
                    GCInfoTimerCallback = (_options.CustomHandler.OnGCInfoTimerAsync, _options.CustomHandler.OnException),
                    ProcessInfoTimerCallback = (_options.CustomHandler.OnProcessInfoTimerAsync, _options.CustomHandler.OnException),
                    ThreadInfoTimerCallback = (_options.CustomHandler.OnThreadInfoTimerAsync, _options.CustomHandler.OnException),
                    ProfilerDiagnosticsTimerCallback = (_options.CustomHandler.OnProfilerDiagnosticsTimerAsync, _options.CustomHandler.OnException),
                },
                ClrTrackerType.Custom when _options.CustomHandler is null => throw new ArgumentException($"{nameof(ClrTrackerType.Custom)}: {_options.CustomHandler} is null, you must set custom Handler."),
                _ => throw new NotImplementedException($"{nameof(ClrTrackerType)}: {_options.TrackerType} not implemented."),
            };
            profilerOptions.EnabledFeatures = _options.EnabledFeatures;
            profilerOptions.AdditionalProfilerFactories = _options.AdditionalProfilerFactories;

            _profilerTracker = new ProfilerTracker(profilerOptions);
            _enabled = true;
        }
    }

    public void StartTracker()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_enabled) return;
            LogMessages.StartTracker(_logger);
            _profilerTracker!.Start();
        }
    }

    public void StopTracker()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_enabled) return;
            LogMessages.StopTracker(_logger);
            _profilerTracker!.Stop();
        }
    }

    public void RestartTracker()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_enabled) return;
            LogMessages.RestartTracker(_logger);
            _profilerTracker!.Restart();
        }
    }

    public void CancelTracker()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_enabled) return;
            LogMessages.CancelTracker(_logger);
            _profilerTracker!.Cancel();
        }
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
            ProfilerDiagnosticsTimerCallback = (datadogTrackerHandler.OnProfilerDiagnosticsTimerAsync, datadogTrackerHandler.OnException),
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
            ProfilerDiagnosticsTimerCallback = (loggerTrackerHandler.OnProfilerDiagnosticsTimerAsync, loggerTrackerHandler.OnException),
        };
    }

    internal int ProfilerCount
    {
        get
        {
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_enabled)
                {
                    return 0;
                }

                var profilerCount = 0;
                _profilerTracker!.Status(_ => profilerCount++);
                return profilerCount;
            }
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;

            _profilerTracker?.Dispose();
            _profilerTracker = null;
            _enabled = false;
            _disposed = true;
        }
    }
}
