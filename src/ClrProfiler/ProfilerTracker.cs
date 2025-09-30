using ClrProfiler.Statistics;
using System.Diagnostics.CodeAnalysis;

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
    /// <summary>
    /// Singleton instance access.
    /// </summary>
    public static Lazy<ProfilerTracker> Current { get; } = new(() => new ProfilerTracker());

    /// <summary>
    /// Options for the tracker
    /// </summary>
    public static ProfilerTrackerOptions Options { get; set; } = new ProfilerTrackerOptions();

    private readonly IProfiler[] profilerStats;
    private int initializedNum;

    private ProfilerTracker()
    {
        // list Stats
        profilerStats = [
            // event
            new GCEventProfiler(Options.GCEventCallback.OnSuccess, Options.GCEventCallback.OnError),
            new ThreadPoolEventProfiler(Options.ThreadPoolEventCallback.OnSuccess, Options.ThreadPoolEventCallback.OnError),
            new ContentionEventProfiler(Options.ContentionEventCallback.OnSuccess, Options.ContentionEventCallback.OnError),
            // timer
            new ThreadInfoTimerProfiler(Options.ThreadInfoTimerCallback.OnSuccess, Options.ThreadInfoTimerCallback.OnError, Options.TimerOption),
            new GCInfoTimerProfiler(Options.GCInfoTimerCallback.OnSuccess, Options.GCInfoTimerCallback.OnError, Options.TimerOption),
            new ProcessInfoTimerProfiler(Options.ProcessInfoTimerCallback.OnSuccess, Options.ProcessInfoTimerCallback.OnError, Options.TimerOption),
        ];
    }

    /// <summary>
    /// Start tracking.
    /// </summary>
    public void Start()
    {
        // offer thread safe single access
        Interlocked.Increment(ref initializedNum);
        if (initializedNum != 1) return;

        // reset initialization status if cancelled.
        Options.CancellationTokenSource.Token.Register(() => initializedNum = 0);

        foreach (var profile in profilerStats)
        {
            profile.Start();
            // FireAndForget
            _ = profile.ReadResultAsync(Options.CancellationTokenSource.Token);
        }
    }
    /// <summary>
    /// Restart tracking.
    /// </summary>
    public void Restart()
    {
        if (initializedNum != 1) return;

        foreach (var stat in profilerStats)
        {
            stat.Restart();
        }
    }
    /// <summary>
    /// Stop tracking.
    /// </summary>
    public void Stop()
    {
        if (initializedNum != 1) return;

        foreach (var stat in profilerStats)
        {
            stat.Stop();
        }
    }

    /// <summary>
    /// Cancel tracking.
    /// </summary>
    public void Cancel()
    {
        Options.CancellationTokenSource.Cancel();
    }

    /// <summary>
    /// Reset tracking.
    /// Available when existing cancellation token source is cancelled.
    /// </summary>
    /// <param name="cts"></param>
    public bool Reset(CancellationTokenSource cts)
    {
        if (Options.CancellationTokenSource.IsCancellationRequested)
        {
            Options.CancellationTokenSource = cts;
            return true;
        }
        return false;
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
