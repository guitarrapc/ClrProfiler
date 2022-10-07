using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;
using System.Diagnostics;
using System.Threading.Channels;

namespace ClrProfiler.TimerListeners;

public class ProcessInfoTimerListener : TimerListenerBase, IDisposable, IChannelReader
{
    static int initializedCount;
    static Timer? timer;

    private static readonly Process _process = Process.GetCurrentProcess();
    private static TimeSpan _oldCPUTime = TimeSpan.Zero;
    private static DateTime _lastMonitorTime = DateTime.UtcNow;
    private static double _cpu = 0;
    private static readonly double RefreshRate = TimeSpan.FromSeconds(1).TotalMilliseconds;

    public ChannelReader<ProcessInfoStatistics>? Reader { get; set; }

    private readonly Channel<ProcessInfoStatistics> _channel;
    private readonly Func<ProcessInfoStatistics, Task> _onEventEmit;
    private readonly Action<Exception> _onEventError;
    private readonly TimeSpan _dueTime;
    private readonly TimeSpan _intervalPeriod;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dueTime">The amount of time delay before timer starts.</param>
    /// <param name="intervalPeriod">The time inteval between the invocation of timer.</param>
    public ProcessInfoTimerListener(Func<ProcessInfoStatistics, Task> onEventEmit, Action<Exception> onEventError, TimeSpan dueTime, TimeSpan intervalPeriod)
    {
        _onEventEmit = onEventEmit;
        _onEventError = onEventError;
        _dueTime = dueTime;
        _intervalPeriod = intervalPeriod;
        _channel = Channel.CreateBounded<ProcessInfoStatistics>(new BoundedChannelOptions(50)
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

            _channel.Writer.TryWrite(stat);
        }
        catch (Exception ex)
        {
            _onEventError?.Invoke(ex);
        }
    }
}
