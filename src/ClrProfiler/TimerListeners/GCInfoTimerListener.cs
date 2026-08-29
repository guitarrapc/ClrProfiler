using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;
using System.Runtime;
using System.Threading.Channels;

namespace ClrProfiler.TimerListeners;

public class GCInfoTimerListener : TimerListenerBase, IDisposable, IChannelReader
{
    private Timer? _timer;
    private readonly object _timerLock = new();
    private bool _disposed;

    public ChannelReader<GCInfoStatistics>? Reader { get; set; }

    private readonly BoundedChannelDispatcher<GCInfoStatistics> _dispatcher;
    private readonly Action<Exception> _onEventError;
    private readonly TimeSpan _dueTime;
    private readonly TimeSpan _intervalPeriod;

    private readonly Func<int>? _getLastGCPercentTimeInGC;

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
    public GCInfoTimerListener(Func<GCInfoStatistics, Task> onEventEmit, Action<Exception> onEventError, TimeSpan dueTime, TimeSpan intervalPeriod)
    {
        _onEventError = onEventError;
        _dueTime = dueTime;
        _intervalPeriod = intervalPeriod;
        _dispatcher = new BoundedChannelDispatcher<GCInfoStatistics>(50, singleWriter: true, onEventEmit, onEventError);

        // GetLastGCPercentTimeInGC has no public equivalent with the same last-GC semantics
        // (GCMemoryInfo.PauseTimePercentage is cumulative), so it stays on reflection.
        var methodGetLastGCPercentTimeInGC = typeof(GC).GetMethod("GetLastGCPercentTimeInGC", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
        _getLastGCPercentTimeInGC = (Func<int>?)methodGetLastGCPercentTimeInGC?.CreateDelegate(typeof(Func<int>));
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
            _timer ??= new Timer(static state => ((GCInfoTimerListener)state!).OnTimer(), this, _dueTime, _intervalPeriod);
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
            var gcmode = GCSettings.IsServerGC ? GCMode.Server : GCMode.Workstation;
            // https://docs.microsoft.com/en-us/dotnet/api/system.runtime.gcsettings.largeobjectheapcompactionmode
            var compactionMode = GCSettings.LargeObjectHeapCompactionMode;
            var latencyMode = GCSettings.LatencyMode;
            var heapSize = GC.GetTotalMemory(false); // bytes
            var totalAllocationBytes = GC.GetTotalAllocatedBytes(false); // bytes. false = approximate. true have performance penalty.
            var gen0Count = GC.CollectionCount(0);
            var gen1Count = GC.CollectionCount(1);
            var gen2Count = GC.CollectionCount(2);
            // Generation sizes after the most recent GC, from the public API instead of private
            // reflection. GenerationInfo is empty until the first GC has happened.
            var generationInfo = GC.GetGCMemoryInfo().GenerationInfo;
            var gen0Size = GetGenerationSizeAfterBytes(generationInfo, 0);
            var gen1Size = GetGenerationSizeAfterBytes(generationInfo, 1);
            var gen2Size = GetGenerationSizeAfterBytes(generationInfo, 2);
            var lohSize = GetGenerationSizeAfterBytes(generationInfo, 3);
            var timeInGc = _getLastGCPercentTimeInGC?.Invoke() ?? 0;
            var totalPauseTimeMillisec = GC.GetTotalPauseDuration().TotalMilliseconds;
            var stat = new GCInfoStatistics(date, gcmode, compactionMode, latencyMode, heapSize, totalAllocationBytes, gen0Count, gen1Count, gen2Count, timeInGc, gen0Size, gen1Size, gen2Size, lohSize, totalPauseTimeMillisec);

            _dispatcher.TryWrite(stat);
        }
        catch (Exception ex)
        {
            _onEventError?.Invoke(ex);
        }
    }

    private static ulong GetGenerationSizeAfterBytes(ReadOnlySpan<GCGenerationInfo> generationInfo, int index)
    {
        if ((uint)index >= (uint)generationInfo.Length) return 0UL;
        var sizeAfterBytes = generationInfo[index].SizeAfterBytes;
        return sizeAfterBytes > 0 ? (ulong)sizeAfterBytes : 0UL;
    }
}
