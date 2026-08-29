using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;

namespace ClrProfiler.TimerListeners;

/// <summary>
/// Samples <see cref="IProfiler.DroppedEventCount"/> for every profiler a tracker owns, so the loss
/// each listener already counts internally becomes an exported metric instead of an invisible number.
/// </summary>
/// <remarks>
/// The observed profilers include this listener's own profiler. Its reader is independent of the
/// other listeners' readers, so a listener whose reader stalls or dies still has its rising count
/// reported here.
/// </remarks>
public class ProfilerDiagnosticsTimerListener : TimerListenerBase, IDisposable, IChannelReader
{
    private Timer? _timer;
    private readonly object _timerLock = new();
    private bool _disposed;

    private readonly BoundedChannelDispatcher<ProfilerDiagnosticsStatistics> _dispatcher;
    private readonly Action<Exception> _onEventError;
    private readonly TimeSpan _dueTime;
    private readonly TimeSpan _intervalPeriod;
    private readonly IReadOnlyList<IProfiler?> _profilers;

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
    /// <param name="profilers">
    /// Profilers to sample. The list is read on every tick rather than copied, so a tracker can pass
    /// the backing array it is still populating; unfilled slots are skipped.
    /// </param>
    public ProfilerDiagnosticsTimerListener(Func<ProfilerDiagnosticsStatistics, Task> onEventEmit, Action<Exception> onEventError, TimeSpan dueTime, TimeSpan intervalPeriod, IReadOnlyList<IProfiler?> profilers)
    {
        _onEventError = onEventError;
        _dueTime = dueTime;
        _intervalPeriod = intervalPeriod;
        _profilers = profilers;
        _dispatcher = new BoundedChannelDispatcher<ProfilerDiagnosticsStatistics>(50, singleWriter: true, onEventEmit, onEventError);
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
            _timer ??= new Timer(static state => ((ProfilerDiagnosticsTimerListener)state!).OnTimer(), this, _dueTime, _intervalPeriod);
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
            // Indexed rather than foreach: IReadOnlyList<T> enumeration allocates an enumerator, and
            // this runs on a timer thread shared with the profiled application.
            for (var i = 0; i < _profilers.Count; i++)
            {
                var profiler = _profilers[i];
                if (profiler is null) continue;

                _dispatcher.TryWrite(new ProfilerDiagnosticsStatistics(date, profiler.Name, profiler.DroppedEventCount));
            }
        }
        catch (Exception ex)
        {
            // A throwing error callback must not unwind into the timer thread.
            ProfilerCallbacks.ReportError(_onEventError, ex);
        }
    }
}
