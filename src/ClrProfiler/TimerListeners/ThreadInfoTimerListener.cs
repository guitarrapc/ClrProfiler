using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;
using System.Threading.Channels;

namespace ClrProfiler.TimerListeners;

public class ThreadInfoTimerListener : TimerListenerBase, IDisposable, IChannelReader
{
    private Timer? _timer;
    private readonly object _timerLock = new();
    private bool _disposed;

    public ChannelReader<ThreadInfoStatistics>? Reader { get; set; }

    private readonly BoundedChannelDispatcher<ThreadInfoStatistics> _dispatcher;
    private readonly Action<Exception> _onEventError;
    private readonly TimeSpan _dueTime;
    private readonly TimeSpan _intervalPeriod;

    /// <summary>
    /// Gets the cumulative number of samples evicted from the bounded delivery channel.
    /// </summary>
    public long DroppedEventCount => _dispatcher.DroppedEventCount;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="onEventEmit">Trigger when Event emitted</param>
    /// <param name="onEventError">Trigger when Event has error</param>
    /// <param name="dueTime">The amount of time delay before timer starts.</param>
    /// <param name="intervalPeriod">The time inteval between the invocation of timer.</param>
    public ThreadInfoTimerListener(Func<ThreadInfoStatistics, Task> onEventEmit, Action<Exception> onEventError, TimeSpan dueTime, TimeSpan intervalPeriod)
    {
        _onEventError = onEventError;
        _dueTime = dueTime;
        _intervalPeriod = intervalPeriod;
        _dispatcher = new BoundedChannelDispatcher<ThreadInfoStatistics>(50, singleWriter: true, onEventEmit, onEventError);
    }

    protected override void OnEventWritten()
    {
        lock (_timerLock)
        {
            if (_disposed)
            {
                Enabled = false;
                throw new ObjectDisposedException(GetType().FullName);
            }
            _timer ??= new Timer(static state => ((ThreadInfoTimerListener)state!).OnTimer(), this, _dueTime, _intervalPeriod);
        }
    }

    private void OnTimer()
    {
        lock (_timerLock)
        {
            if (_disposed || !Enabled) return;
            _eventWritten?.Invoke();
        }
    }

    public void Dispose()
    {
        Timer? timer;
        lock (_timerLock)
        {
            if (_disposed) return;

            _disposed = true;
            Enabled = false;
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
    }

    public ValueTask OnReadResultAsync(CancellationToken cancellationToken) => _dispatcher.ReadAllAsync(cancellationToken);

    public override void EventCreatedHandler()
    {
        try
        {
            var date = DateTime.Now;

            ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out var availableCompletionPortThreads);
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);
            // netcoreapp3.0 and above: get threadpool property `ThreadPool.ThreadCount` https://github.com/dotnet/corefx/pull/37401/files
            var threadCount = ThreadPool.ThreadCount;
            var queueLength = ThreadPool.PendingWorkItemCount;
            var completedItemsCount = ThreadPool.CompletedWorkItemCount;
            var lockContentionCount = Monitor.LockContentionCount;
            var stat = new ThreadInfoStatistics(date, availableWorkerThreads, availableCompletionPortThreads, maxWorkerThreads, maxCompletionPortThreads, threadCount, queueLength, completedItemsCount, lockContentionCount);

            _dispatcher.TryWrite(stat);
        }
        catch (Exception ex)
        {
            // A throwing error callback must not unwind into the timer thread.
            ProfilerCallbacks.ReportError(_onEventError, ex);
        }
    }
}
