using ClrProfiler.Statistics;

namespace ClrProfiler;

/// <summary>
/// Register Options for tracker callbacks and Cancellation token
/// </summary>
public class ProfilerTrackerOptions
{
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

public class ProfilerTracker
{
    private enum TrackerState
    {
        NotStarted,
        Running,
        Stopped,
        Cancelled,
    }

    /// <summary>
    /// Singleton instance access.
    /// </summary>
    public static Lazy<ProfilerTracker> Current { get; } = new(() => new ProfilerTracker());

    /// <summary>
    /// Options for the tracker
    /// </summary>
    public static ProfilerTrackerOptions Options { get; set; } = new ProfilerTrackerOptions();

    private readonly IProfiler[] profilerStats;
    private readonly Task?[] readerTasks;
    private readonly object lifecycleLock = new();
    private TrackerState state = TrackerState.NotStarted;

    private ProfilerTracker()
        : this([
            // event
            new GCEventProfiler(Options.GCEventCallback.OnSuccess, Options.GCEventCallback.OnError),
            new ThreadPoolEventProfiler(Options.ThreadPoolEventCallback.OnSuccess, Options.ThreadPoolEventCallback.OnError),
            new ContentionEventProfiler(Options.ContentionEventCallback.OnSuccess, Options.ContentionEventCallback.OnError),
            // timer
            new ThreadInfoTimerProfiler(Options.ThreadInfoTimerCallback.OnSuccess, Options.ThreadInfoTimerCallback.OnError, Options.TimerOption),
            new GCInfoTimerProfiler(Options.GCInfoTimerCallback.OnSuccess, Options.GCInfoTimerCallback.OnError, Options.TimerOption),
            new ProcessInfoTimerProfiler(Options.ProcessInfoTimerCallback.OnSuccess, Options.ProcessInfoTimerCallback.OnError, Options.TimerOption),
        ])
    {
    }

    internal ProfilerTracker(IProfiler[] profilers)
    {
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
            if (state == TrackerState.Running || state == TrackerState.Cancelled) return;

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
                readerTasks[i] = profile.ReadResultAsync(Options.CancellationTokenSource.Token);
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
            if (state == TrackerState.Cancelled) return;

            if (state == TrackerState.Running)
            {
                foreach (var stat in profilerStats)
                {
                    stat.Stop();
                }
            }
            state = TrackerState.Cancelled;
            cancellationTokenSource = Options.CancellationTokenSource;
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
            if (!Options.CancellationTokenSource.IsCancellationRequested) return false;
            if (readerTasks.Any(task => task is not null && !task.IsCompleted)) return false;

            Options.CancellationTokenSource = cts;
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
}
