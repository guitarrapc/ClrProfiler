using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;
using ClrProfiler.TimerListeners;
using System.Diagnostics;

namespace ClrProfiler;

public interface IProfiler : IDisposable
{
    /// <summary>
    /// Profiler Name
    /// </summary>
    string Name { get; }
    /// <summary>
    /// Profiler status
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// Start Profiling
    /// </summary>
    void Start();
    /// <summary>
    /// Restart Profiling
    /// </summary>
    void Restart();
    /// <summary>
    /// Stop Profiling
    /// </summary>
    void Stop();
    /// <summary>
    /// Read Profiler statistics
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task ReadResultAsync(CancellationToken cancellationToken);
}
public class GCEventProfiler : IProfiler
{
    private readonly GCEventListener? listener;

    public string Name { get; } = nameof(GCEventProfiler);
    public bool Enabled => listener == null ? false : listener.Enabled;

    public GCEventProfiler(Func<GCEventStatistics, Task> onEventEmit, Action<Exception> onEventError)
    {
        // enable only emit callback is exists.
        if (onEventEmit != null)
        {
            listener = new GCEventListener(onEventEmit, onEventError);
        }
    }
    public void Restart()
    {
        listener?.Restart();
    }

    public void Start()
    {
        listener?.RunWithCallback(eventData => listener.EventCreatedHandler(eventData), () =>
        {
            Debug.WriteLine($"Start: {nameof(GCEventProfiler)}");
        });
    }

    public void Stop()
    {
        listener?.Stop();
    }

    public async Task ReadResultAsync(CancellationToken cancellationToken)
    {
        if (listener != null)
        {
            await listener.OnReadResultAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        listener?.Dispose();
    }
}

public class ThreadPoolEventProfiler : IProfiler
{
    private readonly ThreadPoolEventListener? listener;

    public string Name { get; } = nameof(ThreadPoolEventProfiler);
    public bool Enabled => listener == null ? false : listener.Enabled;

    public ThreadPoolEventProfiler(Func<ThreadPoolEventStatistics, Task> onEventEmit, Action<Exception> onEventError)
    {
        // enable only emit callback is exists.
        if (onEventEmit != null)
        {
            listener = new ThreadPoolEventListener(onEventEmit, onEventError);
        }
    }
    public void Restart()
    {
        listener?.Restart();
    }

    public void Start()
    {
        listener?.RunWithCallback(eventData => listener.EventCreatedHandler(eventData), () =>
        {
            Debug.WriteLine($"Start: {nameof(ThreadPoolEventProfiler)}");
        });
    }

    public void Stop()
    {
        listener?.Stop();
    }

    public async Task ReadResultAsync(CancellationToken cancellationToken)
    {
        if (listener != null)
        {
            await listener.OnReadResultAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        listener?.Dispose();
    }
}

public class ContentionEventProfiler : IProfiler
{
    private readonly ContentionEventListener? listener;

    public string Name { get; } = nameof(ContentionEventProfiler);
    public bool Enabled => listener == null ? false : listener.Enabled;

    public ContentionEventProfiler(Func<ContentionEventStatistics, Task> onEventEmit, Action<Exception> onEventError)
    {
        // enable only emit callback is exists.
        if (onEventEmit != null)
        {
            listener = new ContentionEventListener(onEventEmit, onEventError);
        }
    }
    public void Restart()
    {
        listener?.Restart();
    }

    public void Start()
    {
        listener?.RunWithCallback(eventData => listener.EventCreatedHandler(eventData), () =>
        {
            Debug.WriteLine($"Start: {nameof(ContentionEventProfiler)}");
        });
    }

    public void Stop()
    {
        listener?.Stop();
    }

    public async Task ReadResultAsync(CancellationToken cancellationToken)
    {
        if (listener != null)
        {
            await listener.OnReadResultAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        listener?.Dispose();
    }
}

public class ThreadInfoTimerProfiler : IProfiler
{
    private readonly ThreadInfoTimerListener? listener;

    public string Name { get; } = nameof(ThreadInfoTimerProfiler);
    public bool Enabled => listener == null ? false : listener.Enabled;

    public ThreadInfoTimerProfiler(Func<ThreadInfoStatistics, Task> onEventEmit, Action<Exception> onEventError, (TimeSpan dueTime, TimeSpan interval) options)
    {
        // enable only emit callback is exists.
        if (onEventEmit != null)
        {
            listener = new ThreadInfoTimerListener(onEventEmit, onEventError, options.dueTime, options.interval);
        }
    }

    public void Restart()
    {
        listener?.Restart();
    }

    public void Start()
    {
        listener?.RunWithCallback(() => listener.EventCreatedHandler(), () =>
        {
            Debug.WriteLine($"Start: {nameof(ThreadInfoTimerProfiler)}");
        });
    }

    public void Stop()
    {
        listener?.Stop();
    }

    public async Task ReadResultAsync(CancellationToken cancellationToken)
    {
        if (listener != null)
        {
            await listener.OnReadResultAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        listener?.Dispose();
    }
}

public class GCInfoTimerProfiler : IProfiler
{
    private readonly GCInfoTimerListener? listener;

    public string Name { get; } = nameof(GCInfoTimerProfiler);
    public bool Enabled => listener == null ? false : listener.Enabled;

    public GCInfoTimerProfiler(Func<GCInfoStatistics, Task> onEventEmit, Action<Exception> onEventError, (TimeSpan dueTime, TimeSpan interval) options)
    {
        // enable only emit callback is exists.
        if (onEventEmit != null)
        {
            listener = new GCInfoTimerListener(onEventEmit, onEventError, options.dueTime, options.interval);
        }
    }

    public void Restart()
    {
        listener?.Restart();
    }

    public void Start()
    {
        listener?.RunWithCallback(() => listener.EventCreatedHandler(), () =>
        {
            Debug.WriteLine($"Start: {nameof(GCInfoTimerProfiler)}");
        });
    }

    public void Stop()
    {
        listener?.Stop();
    }

    public async Task ReadResultAsync(CancellationToken cancellationToken)
    {
        if (listener != null)
        {
            await listener.OnReadResultAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        listener?.Dispose();
    }
}

public class ProcessInfoTimerProfiler : IProfiler
{
    private readonly ProcessInfoTimerListener? listener;

    public string Name { get; } = nameof(ProcessInfoTimerProfiler);
    public bool Enabled => listener == null ? false : listener.Enabled;

    public ProcessInfoTimerProfiler(Func<ProcessInfoStatistics, Task> onEventEmit, Action<Exception> onEventError, (TimeSpan dueTime, TimeSpan interval) options)
    {
        // enable only emit callback is exists.
        if (onEventEmit != null)
        {
            listener = new ProcessInfoTimerListener(onEventEmit, onEventError, options.dueTime, options.interval);
        }
    }

    public void Restart()
    {
        listener?.Restart();
    }

    public void Start()
    {
        listener?.RunWithCallback(() => listener.EventCreatedHandler(), () =>
        {
            Debug.WriteLine($"Start: {nameof(ProcessInfoTimerProfiler)}");
        });
    }

    public void Stop()
    {
        listener?.Stop();
    }

    public async Task ReadResultAsync(CancellationToken cancellationToken)
    {
        if (listener != null)
        {
            await listener.OnReadResultAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        listener?.Dispose();
    }
}
