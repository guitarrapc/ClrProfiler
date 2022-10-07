using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;
using System.Threading.Channels;

namespace ClrProfiler.TimerListeners;

public class ThreadInfoTimerListener : TimerListenerBase, IDisposable, IChannelReader
{
    static int initializedCount;
    static Timer? timer;

    public ChannelReader<ThreadInfoStatistics>? Reader { get; set; }

    private readonly Channel<ThreadInfoStatistics> _channel;
    private readonly Func<ThreadInfoStatistics, Task> _onEventEmit;
    private readonly Action<Exception> _onEventError;
    private readonly TimeSpan _dueTime;
    private readonly TimeSpan _intervalPeriod;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dueTime">The amount of time delay before timer starts.</param>
    /// <param name="intervalPeriod">The time inteval between the invocation of timer.</param>
    public ThreadInfoTimerListener(Func<ThreadInfoStatistics, Task> onEventEmit, Action<Exception> onEventError, TimeSpan dueTime, TimeSpan intervalPeriod)
    {
        _onEventEmit = onEventEmit;
        _onEventError = onEventError;
        _dueTime = dueTime;
        _intervalPeriod = intervalPeriod;
        _channel = Channel.CreateBounded<ThreadInfoStatistics>(new BoundedChannelOptions(50)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    }

    protected override void OnEventWritten()
    {
        // allow only 1 execution
        var count = Interlocked.Increment(ref initializedCount);
        if (count != 1) return;

        timer = new Timer(_ =>
        {
            if (!Enabled) return;
            _eventWritten?.Invoke();
        }, null, _dueTime, _intervalPeriod);
    }

    public void Dispose()
    {
        var count = Interlocked.Decrement(ref initializedCount);
        if (count == 0)
        {
            var target = Interlocked.Exchange(ref timer, null);
            if (target != null)
            {
                target.Dispose();
            }
        }
    }

    public async ValueTask OnReadResultAsync(CancellationToken cancellationToken)
    {
        // read from channel
        while (Enabled && await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (Enabled && _channel.Reader.TryRead(out var value))
            {
                if (_onEventEmit != null)
                {
                    await _onEventEmit.Invoke(value);
                }
            }
        }
    }

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

            _channel.Writer.TryWrite(stat);
        }
        catch (Exception ex)
        {
            _onEventError?.Invoke(ex);
        }
    }
}
