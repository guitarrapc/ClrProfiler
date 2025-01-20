using ClrProfiler.Statistics;
using System.Diagnostics.Tracing;
using System.Threading.Channels;

namespace ClrProfiler.EventListeners;

/// <summary>
/// EventListener to collect Garbage Collection events. <see cref="GCEventStatistics"/>.
/// https://docs.microsoft.com/en-us/dotnet/framework/performance/garbage-collection-etw-events
/// </summary>
public class GCEventListener : ProfileEventListenerBase, IChannelReader
{
    private readonly Channel<GCEventStatistics> _channel;
    private readonly Func<GCEventStatistics, Task> _onEventEmit;
    private readonly Action<Exception> _onEventError;
    long timeGCStart = 0;
    uint reason = 0;
    uint type = 0;

    // suspend
    long suspendTimeGCStart = 0;
    uint suspendReason = 0;
    uint suspendCount = 0;

    public GCEventListener(Func<GCEventStatistics, Task> onEventEmit, Action<Exception> onEventError) : base("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, ClrRuntimeEventKeywords.GC)
    {
        _onEventEmit = onEventEmit;
        _onEventError = onEventError;
        var channelOption = new BoundedChannelOptions(50)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        };
        _channel = Channel.CreateBounded<GCEventStatistics>(channelOption);
    }

    // GC Flow
    // Foreground (Blocking) GC flow (all Gen 0/1 GCs and full blocking GCs)
    // * GCSuspendEE_V1
    // * GCSuspendEEEnd_V1 <– suspension is done
    // * GCStart_V1
    // * GCEnd_V1 <– actual GC is done
    // * GCRestartEEBegin_V1
    // * GCRestartEEEnd_V1 <– resumption is done.
    // 
    // Background GC flow (Gen 2)
    // * GCSuspendEE_V1
    // * GCSuspendEEEnd_V1
    // * GCStart_V1 <– Background GC starts
    // * GCRestartEEBegin_V1
    // * GCRestartEEEnd_V1 <– done with the initial suspension
    // * GCSuspendEE_V1
    // * GCSuspendEEEnd_V1
    // * GCRestartEEBegin_V1
    // * GCRestartEEEnd_V1 <– done with Background GC’s own suspension
    // * GCSuspendEE_V1
    // * GCSuspendEEEnd_V1 <– suspension for Foreground GC is done
    // * GCStart_V1
    // * GCEnd_V1 <– Foreground GC is done
    // * GCRestartEEBegin_V1
    // * GCRestartEEEnd_V1 <– resumption for Foreground GC is done
    // * GCEnd_V1 <– Background GC ends
    /// <summary>
    /// GC Event Handler
    /// </summary>
    /// <see>
    /// https://docs.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals?redirectedfrom=MSDN#background_garbage_collection
    /// https://mattwarren.org/2016/06/20/Visualising-the-dotNET-Garbage-Collector/
    /// </see>
    public override void EventCreatedHandler(EventWrittenEventArgs eventData)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(eventData.EventName)) return;

            // GCStart & GCEnd = Actual GC
            // GCSuspendEEBegin && GCRestartEEEnd = GC Suspension + Pause (include GC Start-End)
            // NOTE: HeapStat will retrieve in GCInfoTimerListener
            if (eventData.EventName.StartsWith("GCStart_", StringComparison.OrdinalIgnoreCase)) // GCStart_V1 / V2 ...
            {
                timeGCStart = eventData.TimeStamp.Ticks;
                reason = uint.Parse(eventData.Payload?[2]?.ToString() ?? "0");
                type = uint.Parse(eventData.Payload?[3]?.ToString() ?? "0");
            }
            else if (eventData.EventName.StartsWith("GCEnd_", StringComparison.OrdinalIgnoreCase)) // GCEnd_V1 / V2 ...
            {
                long timeGCEnd = eventData.TimeStamp.Ticks;
                var gcIndex = uint.Parse(eventData.Payload?[0]?.ToString() ?? "0");
                var generation = uint.Parse(eventData.Payload?[1]?.ToString() ?? "0");
                var duration = (double)(timeGCEnd - timeGCStart) / 10.0 / 1000.0;
                var stat = new GCStartEndStatistics(gcIndex, type, generation, reason, duration, timeGCStart, timeGCEnd);

                // write to channel
                _channel.Writer.TryWrite(new GCEventStatistics(GCEventType.GCStartEnd, stat, new()));
            }
            else if (eventData.EventName.StartsWith("GCSuspendEEBegin", StringComparison.OrdinalIgnoreCase))
            {
                suspendTimeGCStart = eventData.TimeStamp.Ticks;
                suspendReason = uint.Parse(eventData.Payload?[0]?.ToString() ?? "0");
                suspendCount = uint.Parse(eventData.Payload?[1]?.ToString() ?? "0");
            }
            else if (eventData.EventName.StartsWith("GCRestartEEEnd", StringComparison.OrdinalIgnoreCase))
            {
                var suspendEnd = eventData.TimeStamp.Ticks;
                var duration = (double)(suspendEnd - suspendTimeGCStart) / 10.0 / 1000.0;
                var stat = new GCSuspendStatistics(duration, suspendReason, suspendCount);

                // write to channel
                _channel.Writer.TryWrite(new GCEventStatistics(GCEventType.GCSuspend, new(), stat));
            }
        }
        catch (Exception ex)
        {
            _onEventError?.Invoke(ex);
        }
    }

    public async ValueTask OnReadResultAsync(CancellationToken cancellationToken = default)
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
}
