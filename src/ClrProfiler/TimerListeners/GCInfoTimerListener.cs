using ClrProfiler.EventListeners;
using ClrProfiler.Statistics;
using System.Runtime;
using System.Threading.Channels;

namespace ClrProfiler.TimerListeners;

public class GCInfoTimerListener : TimerListenerBase, IDisposable, IChannelReader
{
    static int initializedCount;
    static Timer? timer;

    public ChannelReader<GCInfoStatistics>? Reader { get; set; }

    private readonly Channel<GCInfoStatistics> _channel;
    private readonly Func<GCInfoStatistics, Task> _onEventEmit;
    private readonly Action<Exception> _onEventError;
    private readonly TimeSpan _dueTime;
    private readonly TimeSpan _intervalPeriod;

    private readonly Func<int, ulong>? _getGenerationSizeDelegate;
    private readonly Func<int>? _getLastGCPercentTimeInGC;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="dueTime">The amount of time delay before timer starts.</param>
    /// <param name="intervalPeriod">The time inteval between the invocation of timer.</param>
    public GCInfoTimerListener(Func<GCInfoStatistics, Task> onEventEmit, Action<Exception> onEventError, TimeSpan dueTime, TimeSpan intervalPeriod)
    {
        _onEventEmit = onEventEmit;
        _onEventError = onEventError;
        _dueTime = dueTime;
        _intervalPeriod = intervalPeriod;
        _channel = Channel.CreateBounded<GCInfoStatistics>(new BoundedChannelOptions(50)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

        var methodGetGenerationSize = typeof(GC).GetMethod("GetGenerationSize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
        _getGenerationSizeDelegate = (Func<int, ulong>?)methodGetGenerationSize?.CreateDelegate(typeof(Func<int, ulong>));

        var methodGetLastGCPercentTimeInGC = typeof(GC).GetMethod("GetLastGCPercentTimeInGC", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
        _getLastGCPercentTimeInGC = (Func<int>?)methodGetLastGCPercentTimeInGC?.CreateDelegate(typeof(Func<int>));
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
        initializedCount = 0;
        timer?.Dispose();
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
            var gcmode = GCSettings.IsServerGC ? GCMode.Server : GCMode.Workstation;
            // https://docs.microsoft.com/en-us/dotnet/api/system.runtime.gcsettings.largeobjectheapcompactionmode
            var compactionMode = GCSettings.LargeObjectHeapCompactionMode;
            var latencyMode = GCSettings.LatencyMode;
            var heapSize = GC.GetTotalMemory(false); // bytes
            var gen0Count = GC.CollectionCount(0);
            var gen1Count = GC.CollectionCount(1);
            var gen2Count = GC.CollectionCount(2);
            var memoryInfo = GC.GetGCMemoryInfo();
            var gen0Size = _getGenerationSizeDelegate?.Invoke(0) ?? 0;
            var gen1Size = _getGenerationSizeDelegate?.Invoke(1) ?? 0;
            var gen2Size = _getGenerationSizeDelegate?.Invoke(2) ?? 0;
            var lohSize = _getGenerationSizeDelegate?.Invoke(3) ?? 0;
            var timeInGc = _getLastGCPercentTimeInGC?.Invoke() ?? 0;
            var stat = new GCInfoStatistics(date, gcmode, compactionMode, latencyMode, heapSize, gen0Count, gen1Count, gen2Count, timeInGc, gen0Size, gen1Size, gen2Size, lohSize);

            _channel.Writer.TryWrite(stat);
        }
        catch (Exception ex)
        {
            _onEventError?.Invoke(ex);
        }
    }
}
