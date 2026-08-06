using ClrProfiler.Statistics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Numerics;
using System.Threading.Channels;

namespace ClrProfiler.EventListeners;

/// <summary>
/// Contention events are raised whenever there is contention for System.Threading.Monitor locks or native locks used by the runtime. 
/// Contention occurs when a thread is waiting for a lock while another thread possesses the lock.
/// https://docs.microsoft.com/en-us/dotnet/framework/performance/contention-etw-events
/// </summary>
public class ContentionEventListener : ProfileEventListenerBase, IChannelReader
{
    private const int ContentionFlagCount = byte.MaxValue + 1;
    /// <summary>Picoseconds per nanosecond. Durations accumulate as whole picoseconds.</summary>
    private const double PicosecondsPerNanosecond = 1000D;
    /// <summary>
    /// Upper bound for a single event's contribution, roughly 2.5 hours. Real contention is
    /// orders of magnitude shorter; the cap only exists so a malformed payload cannot push the
    /// accumulators toward overflow, and it takes 1024 capped events in one window to get there.
    /// </summary>
    private const long MaxDurationPicoseconds = long.MaxValue / 1024;

    private readonly Channel<bool> _flushSignal;
    private readonly Func<ContentionEventStatistics, Task> _onEventEmit;
    private readonly Action<Exception> _onEventError;
    private readonly long[] _counts = new long[ContentionFlagCount];
    private readonly long[] _durationSumPs = new long[ContentionFlagCount];
    private readonly long[] _durationMaxPs = new long[ContentionFlagCount];
    private readonly long[] _times = new long[ContentionFlagCount];
    private readonly long[] _activeFlags = new long[ContentionFlagCount / 64];

    public ContentionEventListener(Func<ContentionEventStatistics, Task> onEventEmit, Action<Exception> onEventError) : base("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, ClrRuntimeEventKeywords.Contention)
    {
        _onEventEmit = onEventEmit;
        _onEventError = onEventError;
        var channelOption = new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        };
        _flushSignal = Channel.CreateBounded<bool>(channelOption);
    }

    public override void EventCreatedHandler(EventWrittenEventArgs eventData)
    {
        ProcessEvent(eventData.EventName, eventData.TimeStamp, eventData.Payload);
    }

    internal void ProcessEvent(string? eventName, DateTime timeStamp, IReadOnlyList<object?>? payload)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(eventName) && eventName.StartsWith("ContentionStop_", StringComparison.OrdinalIgnoreCase))
            {
                long time = timeStamp.Ticks;
                var flag = ReadRequiredByte(payload, 0);
                var durationNs = ReadRequiredDouble(payload, 2);
                Aggregate(time, flag, durationNs);
            }
        }
        catch (Exception ex)
        {
            _onEventError?.Invoke(ex);
        }
    }

    private void Aggregate(long time, byte flag, double durationNs)
    {
        var durationPs = ToPicoseconds(durationNs);

        // Duration and timestamp are folded in before the count is incremented. The reader gates a
        // flush on a non-zero count, so this order can only ever attribute a duration to the flush
        // before its own count, never drop it. Incrementing first would let a duration strand until
        // the next event for the same flag arrives.
        Interlocked.Add(ref _durationSumPs[flag], durationPs);
        InterlockedMax(ref _durationMaxPs[flag], durationPs);
        InterlockedMax(ref _times[flag], time);
        if (Interlocked.Increment(ref _counts[flag]) == 1)
        {
            Interlocked.Or(ref _activeFlags[flag / 64], 1L << (flag % 64));
            _flushSignal.Writer.TryWrite(true);
        }
    }

    /// <summary>
    /// Converts a payload duration to whole picoseconds so the per-event fold stays a single
    /// lock-free add. Accumulating the sum as a double would need a compare-exchange loop, which
    /// spins on exactly the path that is by definition already contended. Picoseconds keep the
    /// values the runtime reports exact while leaving the accumulator far from overflow.
    /// Non-finite and negative payloads contribute nothing rather than corrupting the sum.
    /// </summary>
    private static long ToPicoseconds(double durationNs)
    {
        if (!double.IsFinite(durationNs) || durationNs <= 0D)
        {
            return 0L;
        }

        var picoseconds = durationNs * PicosecondsPerNanosecond;
        if (picoseconds >= MaxDurationPicoseconds)
        {
            return MaxDurationPicoseconds;
        }

        return (long)Math.Round(picoseconds, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Raises <paramref name="location"/> to <paramref name="value"/> when it is larger. The loop
    /// only retries when a concurrent writer also raised the value, so it is bounded by the number
    /// of increases rather than by the number of writers.
    /// </summary>
    private static void InterlockedMax(ref long location, long value)
    {
        var current = Volatile.Read(ref location);
        while (current < value)
        {
            var observed = Interlocked.CompareExchange(ref location, value, current);
            if (observed == current)
            {
                return;
            }
            current = observed;
        }
    }

    private static byte ReadRequiredByte(IReadOnlyList<object?>? payload, int index)
    {
        if (payload is null || (uint)index >= (uint)payload.Count)
        {
            throw new InvalidDataException($"Required contention payload at index {index} is missing.");
        }

        var payloadValue = payload[index];
        if (payloadValue is null)
        {
            throw new InvalidDataException($"Required contention payload at index {index} is missing.");
        }

        return payloadValue switch
        {
            byte value => value,
            sbyte value => checked((byte)value),
            ushort value => checked((byte)value),
            short value => checked((byte)value),
            uint value => checked((byte)value),
            int value => checked((byte)value),
            ulong value => checked((byte)value),
            long value => checked((byte)value),
            string value => byte.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException($"Contention payload at index {index} is not an integer."),
        };
    }

    private static double ReadRequiredDouble(IReadOnlyList<object?>? payload, int index)
    {
        if (payload is null || (uint)index >= (uint)payload.Count)
        {
            throw new InvalidDataException($"Required contention payload at index {index} is missing.");
        }

        var payloadValue = payload[index];
        if (payloadValue is null)
        {
            throw new InvalidDataException($"Required contention payload at index {index} is missing.");
        }

        return payloadValue switch
        {
            double value => value,
            float value => value,
            decimal value => (double)value,
            ulong value => value,
            long value => value,
            uint value => value,
            int value => value,
            ushort value => value,
            short value => value,
            byte value => value,
            sbyte value => value,
            string value => double.Parse(value, CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException($"Contention payload at index {index} is not numeric."),
        };
    }

    public async ValueTask OnReadResultAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Keep the reader alive across Stop/Restart. Cancellation owns its lifetime.
            while (await _flushSignal.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_flushSignal.Reader.TryRead(out _))
                {
                    for (var wordIndex = 0; wordIndex < _activeFlags.Length; wordIndex++)
                    {
                        var activeFlags = (ulong)Interlocked.Exchange(ref _activeFlags[wordIndex], 0);
                        while (activeFlags != 0)
                        {
                            var bitIndex = BitOperations.TrailingZeroCount(activeFlags);
                            var flag = (wordIndex * 64) + bitIndex;
                            activeFlags &= activeFlags - 1;

                            var count = Interlocked.Exchange(ref _counts[flag], 0);
                            if (count == 0)
                            {
                                // A producer sets the active bit before incrementing the count, so a
                                // bit can outlive the flush that already drained it. Leaving the
                                // duration accumulators alone here is what lets a duration folded in
                                // just before that flush be reported by the next one.
                                continue;
                            }
                            var durationSumPs = Interlocked.Exchange(ref _durationSumPs[flag], 0);
                            var durationMaxPs = Interlocked.Exchange(ref _durationMaxPs[flag], 0);
                            // Read, not reset: this is the newest timestamp seen for the flag and must
                            // not regress to zero for a flush whose event was counted after a reset.
                            var time = Volatile.Read(ref _times[flag]);
                            var value = new ContentionEventStatistics(
                                time,
                                (byte)flag,
                                count,
                                durationSumPs / PicosecondsPerNanosecond,
                                durationMaxPs / PicosecondsPerNanosecond);
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
