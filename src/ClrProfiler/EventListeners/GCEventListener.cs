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
    // A background GC can overlap a foreground GC, so GC start state must be
    // correlated by the GC index carried by both GCStart and GCEnd. GC indices
    // are sequential and the runtime cannot have this many collections active
    // concurrently, making a fixed array sufficient without per-event allocation.
    private const int GCStartStateCapacity = 64;

    private readonly Channel<GCEventStatistics> _channel;
    private readonly Func<GCEventStatistics, Task> _onEventEmit;
    private readonly Action<Exception> _onEventError;
    private readonly GCStartState[] _gcStartStates = new GCStartState[GCStartStateCapacity];
    private readonly object _gcStartStatesLock = new();

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
        ProcessEvent(eventData.EventName, eventData.TimeStamp, eventData.Payload);
    }

    internal void ProcessEvent(string? eventName, DateTime timeStamp, IReadOnlyList<object?>? payload)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(eventName)) return;

            // GCStart & GCEnd = Actual GC
            // GCSuspendEEBegin && GCRestartEEEnd = GC Suspension + Pause (include GC Start-End)
            // NOTE: HeapStat will retrieve in GCInfoTimerListener
            if (eventName.StartsWith("GCStart_", StringComparison.OrdinalIgnoreCase)) // GCStart_V1 / V2 ...
            {
                var gcIndex = ReadUInt32(payload, 0);
                var startState = new GCStartState(
                    gcIndex,
                    ReadUInt32(payload, 3),
                    ReadUInt32(payload, 2),
                    timeStamp.Ticks);

                lock (_gcStartStatesLock)
                {
                    _gcStartStates[GetGCStartStateSlot(gcIndex)] = startState;
                }
            }
            else if (eventName.StartsWith("GCEnd_", StringComparison.OrdinalIgnoreCase)) // GCEnd_V1 / V2 ...
            {
                long timeGCEnd = timeStamp.Ticks;
                var gcIndex = ReadUInt32(payload, 0);
                var generation = ReadUInt32(payload, 1);
                GCStartState startState;

                lock (_gcStartStatesLock)
                {
                    var slot = GetGCStartStateSlot(gcIndex);
                    startState = _gcStartStates[slot];
                    if (!startState.Active || startState.Index != gcIndex)
                    {
                        // The listener may be enabled in the middle of a GC and
                        // observe its end without having observed its start.
                        return;
                    }

                    _gcStartStates[slot] = default;
                }

                var duration = (double)(timeGCEnd - startState.StartTime) / 10.0 / 1000.0;
                var stat = new GCStartEndStatistics(gcIndex, startState.Type, generation, startState.Reason, duration, startState.StartTime, timeGCEnd);

                // write to channel
                _channel.Writer.TryWrite(new GCEventStatistics(GCEventType.GCStartEnd, stat, new()));
            }
            else if (eventName.StartsWith("GCSuspendEEBegin", StringComparison.OrdinalIgnoreCase))
            {
                suspendTimeGCStart = timeStamp.Ticks;
                suspendReason = uint.Parse(payload?[0]?.ToString() ?? "0");
                suspendCount = uint.Parse(payload?[1]?.ToString() ?? "0");
            }
            else if (eventName.StartsWith("GCRestartEEEnd", StringComparison.OrdinalIgnoreCase))
            {
                var suspendEnd = timeStamp.Ticks;
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

    private static int GetGCStartStateSlot(uint gcIndex) => (int)(gcIndex % GCStartStateCapacity);

    private static uint ReadUInt32(IReadOnlyList<object?>? payload, int index)
    {
        if (payload is null || (uint)index >= (uint)payload.Count)
        {
            return 0;
        }

        return payload[index] switch
        {
            uint value => value,
            int value => checked((uint)value),
            ushort value => value,
            short value => checked((uint)value),
            byte value => value,
            sbyte value => checked((uint)value),
            ulong value => checked((uint)value),
            long value => checked((uint)value),
            null => 0,
            _ => Convert.ToUInt32(payload[index], System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private readonly struct GCStartState(uint index, uint type, uint reason, long startTime)
    {
        public readonly uint Index = index;
        public readonly uint Type = type;
        public readonly uint Reason = reason;
        public readonly long StartTime = startTime;
        public readonly bool Active = true;
    }

    public async ValueTask OnReadResultAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Keep the reader alive across Stop/Restart. Cancellation owns its lifetime.
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_channel.Reader.TryRead(out var value))
                {
                    if (_onEventEmit != null)
                    {
                        try
                        {
                            await _onEventEmit.Invoke(value);
                        }
                        catch (Exception ex)
                        {
                            _onEventError.Invoke(ex);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
