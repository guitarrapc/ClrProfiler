using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;
using System.Diagnostics;
using System.Threading.Channels;

namespace ClrProfiler.TimerListeners;

public class ProcessInfoTimerListener : TimerListenerBase, IDisposable, IChannelReader
{
    private Timer? _timer;
    private readonly object _timerLock = new();
    private bool _disposed;

    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _oldCPUTime;
    private DateTime _lastMonitorTime;
    private double _cpu = 0;
    private static readonly double RefreshRate = TimeSpan.FromSeconds(1).TotalMilliseconds;

    public ChannelReader<ProcessInfoStatistics>? Reader { get; set; }

    private readonly BoundedChannelDispatcher<ProcessInfoStatistics> _dispatcher;
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
    public ProcessInfoTimerListener(Func<ProcessInfoStatistics, Task> onEventEmit, Action<Exception> onEventError, TimeSpan dueTime, TimeSpan intervalPeriod)
    {
        _onEventError = onEventError;
        _dueTime = dueTime;
        _intervalPeriod = intervalPeriod;
        _oldCPUTime = _process.TotalProcessorTime;
        _lastMonitorTime = DateTime.UtcNow;
        _dispatcher = new BoundedChannelDispatcher<ProcessInfoStatistics>(50, singleWriter: true, onEventEmit, onEventError);
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
            _timer ??= new Timer(static state => ((ProcessInfoTimerListener)state!).OnTimer(), this, _dueTime, _intervalPeriod);
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
        _process.Dispose();
    }

    public ValueTask OnReadResultAsync(CancellationToken cancellationToken) => _dispatcher.ReadAllAsync(cancellationToken);

    public override void EventCreatedHandler()
    {
        try
        {
            var date = DateTime.Now;
            _process.Refresh();

            // calculate CPU per RefreshRate
            var now = DateTime.UtcNow;
            var cpuElapsedTime = now.Subtract(_lastMonitorTime).TotalMilliseconds;
            if (cpuElapsedTime > RefreshRate)
            {
                var newCPUTime = _process.TotalProcessorTime;
                var elapsedCPU = (newCPUTime - _oldCPUTime).TotalMilliseconds;
                _cpu = elapsedCPU * 100 / Environment.ProcessorCount / cpuElapsedTime;

                _lastMonitorTime = now;
                _oldCPUTime = newCPUTime;
            }

            var workingSet = _process.WorkingSet64;
            var privateBytes = _process.PrivateMemorySize64;
            var stat = new ProcessInfoStatistics(date, _cpu, workingSet, privateBytes);

            _dispatcher.TryWrite(stat);
        }
        catch (Exception ex)
        {
            _onEventError?.Invoke(ex);
        }
    }
}
