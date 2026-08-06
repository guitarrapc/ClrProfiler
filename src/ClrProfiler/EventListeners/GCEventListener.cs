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
    // A background GC can overlap foreground GCs, so correlate by GC index.
    // Linear probing keeps the state bounded and allocation-free per event while
    // allowing indices separated by the table capacity to remain active together.
    private const int GCStartStateCapacity = 64;

    private readonly Channel<GCEventStatistics> _channel;
    private readonly Func<GCEventStatistics, Task> _onEventEmit;
    private readonly Action<Exception> _onEventError;
    private readonly GCStartStateSlot[] _gcStartStates = new GCStartStateSlot[GCStartStateCapacity];

    // suspend
    private long _suspendOwner;
    private long _suspendTimeGCStart;
    private uint _suspendReason;
    private uint _suspendCount;

    public GCEventListener(Func<GCEventStatistics, Task> onEventEmit, Action<Exception> onEventError) : base("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, ClrRuntimeEventKeywords.GC)
    {
        _onEventEmit = onEventEmit;
        _onEventError = onEventError;
        var channelOption = new BoundedChannelOptions(50)
        {
            SingleReader = true,
            SingleWriter = false,
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
                var gcIndex = ReadRequiredUInt32(payload, 0);
                var startState = new GCStartState(
                    gcIndex,
                    ReadRequiredUInt32(payload, 3),
                    ReadRequiredUInt32(payload, 2),
                    timeStamp.Ticks);

                var storeResult = StoreGCStartState(startState, out var evictedIndex);
                if (storeResult != GCStartStateStoreResult.Stored)
                {
                    throw new InvalidOperationException(storeResult == GCStartStateStoreResult.ReplacedOldest
                        ? $"GC correlation capacity exceeded. Evicted start with index {evictedIndex} to store start with index {gcIndex}."
                        : $"GC correlation state was busy. Dropped start with index {gcIndex}.");
                }
            }
            else if (eventName.StartsWith("GCEnd_", StringComparison.OrdinalIgnoreCase)) // GCEnd_V1 / V2 ...
            {
                long timeGCEnd = timeStamp.Ticks;
                var gcIndex = ReadRequiredUInt32(payload, 0);
                var generation = ReadRequiredUInt32(payload, 1);
                if (!TryTakeGCStartState(gcIndex, out var startState))
                {
                    // The listener may be enabled in the middle of a GC and
                    // observe its end without having observed its start.
                    return;
                }

                var duration = (double)(timeGCEnd - startState.StartTime) / 10.0 / 1000.0;
                var stat = new GCStartEndStatistics(gcIndex, startState.Type, generation, startState.Reason, duration, startState.StartTime, timeGCEnd);

                // write to channel
                _channel.Writer.TryWrite(new GCEventStatistics(GCEventType.GCStartEnd, stat, new()));
            }
            else if (eventName.StartsWith("GCSuspendEEBegin", StringComparison.OrdinalIgnoreCase))
            {
                var reason = ReadRequiredUInt32(payload, 0);
                var count = ReadRequiredUInt32(payload, 1);
                var replacedActiveSuspend = !TryBeginSuspend(timeStamp.Ticks, reason, count);
                if (replacedActiveSuspend)
                {
                    throw new InvalidOperationException("A new GC suspend start replaced an unmatched active suspend start.");
                }
            }
            else if (eventName.StartsWith("GCRestartEEEnd", StringComparison.OrdinalIgnoreCase))
            {
                var suspendEnd = timeStamp.Ticks;
                if (!TryEndSuspend(out var suspendStart, out var suspendReason, out var suspendCount))
                {
                    return;
                }

                var duration = (double)(suspendEnd - suspendStart) / 10.0 / 1000.0;
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

    private GCStartStateStoreResult StoreGCStartState(in GCStartState startState, out uint evictedIndex)
    {
        evictedIndex = 0;
        var firstSlot = (int)(startState.Index % GCStartStateCapacity);
        var owner = GetCorrelationOwner(startState.Index);
        for (var offset = 0; offset < GCStartStateCapacity; offset++)
        {
            ref var slot = ref _gcStartStates[(firstSlot + offset) % GCStartStateCapacity];
            var observedOwner = Volatile.Read(ref slot.Owner);
            if (observedOwner == owner)
            {
                if (Interlocked.CompareExchange(ref slot.Owner, -owner, owner) == owner)
                {
                    WriteGCStartState(ref slot, startState, owner);
                    return GCStartStateStoreResult.Stored;
                }
                continue;
            }

            if (observedOwner == 0 && Interlocked.CompareExchange(ref slot.Owner, -owner, 0) == 0)
            {
                WriteGCStartState(ref slot, startState, owner);
                return GCStartStateStoreResult.Stored;
            }
        }

        var oldestSlotIndex = -1;
        var oldestStartTime = long.MaxValue;
        var oldestOwner = 0L;
        for (var i = 0; i < GCStartStateCapacity; i++)
        {
            ref var slot = ref _gcStartStates[i];
            var observedOwner = Volatile.Read(ref slot.Owner);
            if (observedOwner > 0 && slot.StartTime < oldestStartTime)
            {
                oldestStartTime = slot.StartTime;
                oldestSlotIndex = i;
                oldestOwner = observedOwner;
            }
        }

        if (oldestSlotIndex >= 0)
        {
            ref var oldestSlot = ref _gcStartStates[oldestSlotIndex];
            if (Interlocked.CompareExchange(ref oldestSlot.Owner, -owner, oldestOwner) == oldestOwner)
            {
                evictedIndex = (uint)(oldestOwner - 1);
                WriteGCStartState(ref oldestSlot, startState, owner);
                return GCStartStateStoreResult.ReplacedOldest;
            }
        }

        return GCStartStateStoreResult.Dropped;
    }

    private bool TryTakeGCStartState(uint gcIndex, out GCStartState startState)
    {
        var firstSlot = (int)(gcIndex % GCStartStateCapacity);
        var owner = GetCorrelationOwner(gcIndex);
        for (var offset = 0; offset < GCStartStateCapacity; offset++)
        {
            ref var slot = ref _gcStartStates[(firstSlot + offset) % GCStartStateCapacity];
            if (Volatile.Read(ref slot.Owner) != owner)
            {
                continue;
            }

            if (Interlocked.CompareExchange(ref slot.Owner, -owner, owner) != owner)
            {
                continue;
            }

            startState = new GCStartState(slot.Index, slot.Type, slot.Reason, slot.StartTime);
            Volatile.Write(ref slot.Owner, 0);
            return true;
        }

        startState = default;
        return false;
    }

    private static void WriteGCStartState(ref GCStartStateSlot slot, in GCStartState startState, long owner)
    {
        slot.Index = startState.Index;
        slot.Type = startState.Type;
        slot.Reason = startState.Reason;
        slot.StartTime = startState.StartTime;
        Volatile.Write(ref slot.Owner, owner);
    }

    private static long GetCorrelationOwner(uint index) => (long)index + 1;

    private bool TryBeginSuspend(long startTime, uint reason, uint count)
    {
        var owner = GetCorrelationOwner(count);
        var previousOwner = Interlocked.CompareExchange(ref _suspendOwner, -owner, 0);
        var replacedActive = false;
        if (previousOwner != 0)
        {
            if (previousOwner < 0 || Interlocked.CompareExchange(ref _suspendOwner, -owner, previousOwner) != previousOwner)
            {
                throw new InvalidOperationException("GC suspend correlation state was busy. Dropped the suspend start.");
            }
            replacedActive = true;
        }

        _suspendTimeGCStart = startTime;
        _suspendReason = reason;
        _suspendCount = count;
        Volatile.Write(ref _suspendOwner, owner);
        return !replacedActive;
    }

    private bool TryEndSuspend(out long startTime, out uint reason, out uint count)
    {
        var owner = Volatile.Read(ref _suspendOwner);
        if (owner <= 0 || Interlocked.CompareExchange(ref _suspendOwner, -owner, owner) != owner)
        {
            startTime = 0;
            reason = 0;
            count = 0;
            return false;
        }

        startTime = _suspendTimeGCStart;
        reason = _suspendReason;
        count = _suspendCount;
        Volatile.Write(ref _suspendOwner, 0);
        return true;
    }

    private static uint ReadRequiredUInt32(IReadOnlyList<object?>? payload, int index)
    {
        if (payload is null || (uint)index >= (uint)payload.Count || payload[index] is null)
        {
            throw new InvalidDataException($"Required GC payload at index {index} is missing.");
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
            _ => Convert.ToUInt32(payload[index], System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private readonly struct GCStartState(uint index, uint type, uint reason, long startTime)
    {
        public readonly uint Index = index;
        public readonly uint Type = type;
        public readonly uint Reason = reason;
        public readonly long StartTime = startTime;
    }

    private struct GCStartStateSlot
    {
        public long Owner;
        public uint Index;
        public uint Type;
        public uint Reason;
        public long StartTime;
    }

    private enum GCStartStateStoreResult
    {
        Stored,
        ReplacedOldest,
        Dropped,
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
