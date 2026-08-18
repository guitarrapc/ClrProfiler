using ClrProfiler.Statistics;
using System.Numerics;

namespace ClrProfiler;

/// <summary>
/// Register Options for tracker callbacks and Cancellation token
/// </summary>
public class ProfilerTrackerOptions
{
    /// <summary>
    /// Gets or sets the instrumentation to create and run. Defaults to all features for compatibility.
    /// </summary>
    public ProfilerFeature EnabledFeatures { get; set; } = ProfilerFeature.All;

    /// <summary>
    /// CancellationTokenSource to cancel reading event channel.
    /// </summary>
    public CancellationTokenSource CancellationTokenSource { get; set; } = new CancellationTokenSource();
    /// <summary>
    /// Timer dueTime/interval Options.
    /// </summary>
    public (TimeSpan dueTime, TimeSpan intervalPeriod) TimerOption { get; set; } = (TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    /// <summary>
    /// Callback invoke when Contention Event emitted (generally lock event) and error.
    /// </summary>
    public (Func<ContentionEventStatistics, Task> OnSuccess, Action<Exception> OnError) ContentionEventCallback { get; set; } = (stats => Task.CompletedTask, _ => { });
    /// <summary>
    /// Callback invoke when GC Event emitted and error.
    /// </summary>
    public (Func<GCEventStatistics, Task> OnSuccess, Action<Exception> OnError) GCEventCallback { get; set; } = (stats => Task.CompletedTask, _ => { });
    /// <summary>
    /// Callback invoke when ThreadPool Event emitted and error.
    /// </summary>
    public (Func<ThreadPoolEventStatistics, Task> OnSuccess, Action<Exception> OnError) ThreadPoolEventCallback { get; set; } = (stats => Task.CompletedTask, _ => { });
    /// <summary>
    /// Callback invoke when Timer GCInfo Event emitted and error.
    /// </summary>
    public (Func<GCInfoStatistics, Task> OnSuccess, Action<Exception> OnError) GCInfoTimerCallback { get; set; } = (stats => Task.CompletedTask, _ => { });
    /// <summary>
    /// Callback invoke when Timer ProcessInfo Event emitted and error.
    /// </summary>
    public (Func<ProcessInfoStatistics, Task> OnSuccess, Action<Exception> OnError) ProcessInfoTimerCallback { get; set; } = (stats => Task.CompletedTask, _ => { });
    /// <summary>
    /// Callback invoke when Timer ThreadInfo Event emitted and error.
    /// </summary>
    public (Func<ThreadInfoStatistics, Task> OnSuccess, Action<Exception> OnError) ThreadInfoTimerCallback { get; set; } = (stats => Task.CompletedTask, _ => { });
}

public class ProfilerTracker : IDisposable
{
    private enum TrackerState
    {
        NotStarted,
        Running,
        Stopped,
        Cancelled,
        Disposed,
    }

    private readonly ProfilerTrackerOptions options;
    private readonly IProfiler[] profilerStats;
    private readonly Task?[] readerTasks;
    private readonly object lifecycleLock = new();
    private TrackerState state = TrackerState.NotStarted;

    public ProfilerTracker(ProfilerTrackerOptions? options = null)
    {
        this.options = options ?? new ProfilerTrackerOptions();
        var enabledFeatures = this.options.EnabledFeatures;
        if ((enabledFeatures & ~ProfilerFeature.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ProfilerTrackerOptions.EnabledFeatures), enabledFeatures, "Unknown profiler feature flags were specified.");
        }

        var enabledFeatureCount = BitOperations.PopCount((uint)enabledFeatures);
        profilerStats = enabledFeatureCount == 0
            ? Array.Empty<IProfiler>()
            : new IProfiler[enabledFeatureCount];
        var profilerIndex = 0;
        if ((enabledFeatures & ProfilerFeature.GCEvent) != 0)
        {
            profilerStats[profilerIndex++] = new GCEventProfiler(this.options.GCEventCallback.OnSuccess, this.options.GCEventCallback.OnError);
        }
        if ((enabledFeatures & ProfilerFeature.ThreadPoolEvent) != 0)
        {
            profilerStats[profilerIndex++] = new ThreadPoolEventProfiler(this.options.ThreadPoolEventCallback.OnSuccess, this.options.ThreadPoolEventCallback.OnError);
        }
        if ((enabledFeatures & ProfilerFeature.ContentionEvent) != 0)
        {
            profilerStats[profilerIndex++] = new ContentionEventProfiler(this.options.ContentionEventCallback.OnSuccess, this.options.ContentionEventCallback.OnError);
        }
        if ((enabledFeatures & ProfilerFeature.ThreadInfoTimer) != 0)
        {
            profilerStats[profilerIndex++] = new ThreadInfoTimerProfiler(this.options.ThreadInfoTimerCallback.OnSuccess, this.options.ThreadInfoTimerCallback.OnError, this.options.TimerOption);
        }
        if ((enabledFeatures & ProfilerFeature.GCInfoTimer) != 0)
        {
            profilerStats[profilerIndex++] = new GCInfoTimerProfiler(this.options.GCInfoTimerCallback.OnSuccess, this.options.GCInfoTimerCallback.OnError, this.options.TimerOption);
        }
        if ((enabledFeatures & ProfilerFeature.ProcessInfoTimer) != 0)
        {
            profilerStats[profilerIndex++] = new ProcessInfoTimerProfiler(this.options.ProcessInfoTimerCallback.OnSuccess, this.options.ProcessInfoTimerCallback.OnError, this.options.TimerOption);
        }
        if (profilerIndex != profilerStats.Length)
        {
            for (var i = 0; i < profilerIndex; i++)
            {
                profilerStats[i].Dispose();
            }
            throw new InvalidOperationException($"Profiler feature mapping is incomplete. Expected {profilerStats.Length} profilers but created {profilerIndex}.");
        }
        readerTasks = new Task?[profilerStats.Length];
    }

    internal ProfilerTracker(IProfiler[] profilers, ProfilerTrackerOptions? options = null)
    {
        this.options = options ?? new ProfilerTrackerOptions();
        profilerStats = profilers;
        readerTasks = new Task?[profilers.Length];
    }

    /// <summary>
    /// Start tracking.
    /// </summary>
    public void Start()
    {
        lock (lifecycleLock)
        {
            if (state is TrackerState.Running or TrackerState.Cancelled or TrackerState.Disposed) return;

            if (state == TrackerState.Stopped)
            {
                state = TrackerState.Running;
                foreach (var profile in profilerStats)
                {
                    profile.Restart();
                }
                return;
            }

            state = TrackerState.Running;
            for (var i = 0; i < profilerStats.Length; i++)
            {
                // Keep one reader alive until cancellation so Stop/Restart does not lose it.
                var profile = profilerStats[i];
                readerTasks[i] = profile.ReadResultAsync(options.CancellationTokenSource.Token);
                profile.Start();
            }
        }
    }
    /// <summary>
    /// Restart tracking.
    /// </summary>
    public void Restart()
    {
        lock (lifecycleLock)
        {
            if (state != TrackerState.Stopped) return;

            state = TrackerState.Running;
            foreach (var stat in profilerStats)
            {
                stat.Restart();
            }
        }
    }
    /// <summary>
    /// Stop tracking.
    /// </summary>
    public void Stop()
    {
        lock (lifecycleLock)
        {
            if (state != TrackerState.Running) return;

            state = TrackerState.Stopped;
            foreach (var stat in profilerStats)
            {
                stat.Stop();
            }
        }
    }

    /// <summary>
    /// Cancel tracking.
    /// </summary>
    public void Cancel()
    {
        CancellationTokenSource cancellationTokenSource;
        lock (lifecycleLock)
        {
            if (state is TrackerState.Cancelled or TrackerState.Disposed) return;

            if (state == TrackerState.Running)
            {
                foreach (var stat in profilerStats)
                {
                    stat.Stop();
                }
            }
            state = TrackerState.Cancelled;
            cancellationTokenSource = options.CancellationTokenSource;
        }
        cancellationTokenSource.Cancel();
    }

    /// <summary>
    /// Reset tracking.
    /// Available when existing cancellation token source is cancelled.
    /// </summary>
    /// <param name="cts"></param>
    public bool Reset(CancellationTokenSource cts)
    {
        lock (lifecycleLock)
        {
            if (state != TrackerState.Cancelled) return false;
            if (!options.CancellationTokenSource.IsCancellationRequested) return false;
            if (readerTasks.Any(task => task is not null && !task.IsCompleted)) return false;

            options.CancellationTokenSource = cts;
            state = TrackerState.NotStarted;
            return true;
        }
    }

    /// <summary>
    /// Show profiler status.
    /// </summary>
    /// <param name="action"></param>
    public void Status(Action<(string Name, bool Enabled)> action)
    {
        foreach (var profiler in profilerStats)
        {
            action((profiler.Name, profiler.Enabled));
        }
    }

    public void Dispose()
    {
        CancellationTokenSource cancellationTokenSource;
        lock (lifecycleLock)
        {
            if (state == TrackerState.Disposed) return;

            if (state == TrackerState.Running)
            {
                foreach (var profiler in profilerStats)
                {
                    profiler.Stop();
                }
            }
            state = TrackerState.Disposed;
            cancellationTokenSource = options.CancellationTokenSource;
        }

        cancellationTokenSource.Cancel();
        foreach (var profiler in profilerStats)
        {
            profiler.Dispose();
        }
    }
}
