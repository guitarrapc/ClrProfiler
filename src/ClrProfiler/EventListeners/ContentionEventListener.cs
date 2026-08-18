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
    private const int MaxEnqueueAttempts = 8;
    private const int MaxBatchSize = 1024;
    /// <summary>Picoseconds per nanosecond. Durations accumulate as whole picoseconds.</summary>
    private const double PicosecondsPerNanosecond = 1000D;
    /// <summary>
    /// Upper bound for a single event's contribution, roughly 2.5 hours. Real contention is
    /// orders of magnitude shorter; the cap only exists so a malformed payload cannot push the
    /// accumulator toward overflow when a full reader batch is folded into one window.
    /// </summary>
    private const long MaxDurationPicoseconds = long.MaxValue / MaxBatchSize;

    internal const int EventQueueCapacity = 4 * 1024;

    private readonly BoundedMpscQueue<ContentionSample> _events = new(EventQueueCapacity);
    private readonly Channel<bool> _flushSignal;
    private readonly Func<ContentionEventStatistics, Task> _onEventEmit;
    private readonly Action<Exception> _onEventError;
    private readonly long[] _readerCounts = new long[ContentionFlagCount];
    private readonly long[] _readerDurationSumPs = new long[ContentionFlagCount];
    private readonly long[] _readerDurationMaxPs = new long[ContentionFlagCount];
    private readonly long[] _readerTimes = new long[ContentionFlagCount];
    private readonly ulong[] _readerActiveFlags = new ulong[ContentionFlagCount / 64];
    private long _generation;
    private long _droppedEventCount;
    private int _notificationPending;

    /// <summary>
    /// Gets the cumulative number of contention samples rejected because the fixed event queue
    /// was full or a producer could not reserve a slot within the bounded retry limit.
    /// </summary>
    public long DroppedEventCount => Volatile.Read(ref _droppedEventCount);

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
        _flushSignal.Writer.TryWrite(true);
        _flushSignal.Reader.TryRead(out _);
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
        if (!_events.TryEnqueue(new ContentionSample(time, Volatile.Read(ref _generation), durationPs, flag)))
        {
            Interlocked.Increment(ref _droppedEventCount);
            return;
        }

        if (Interlocked.Exchange(ref _notificationPending, 1) == 0)
        {
            _flushSignal.Writer.TryWrite(true);
        }
    }

    /// <summary>
    /// Converts a payload duration to whole picoseconds before it enters the fixed event queue.
    /// This lets the single reader accumulate durations with integer addition and no rounding drift.
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
    /// Stops the listener and advances the delivery generation.
    /// </summary>
    /// <remarks>
    /// Queued samples retain their generation, so the reader emits pre-stop and post-restart samples
    /// in separate aggregation windows even when both are drained later.
    /// </remarks>
    public override void Stop()
    {
        base.Stop();
        Interlocked.Increment(ref _generation);
    }

    private void AggregateOnReader(in ContentionSample sample)
    {
        var flag = sample.Flag;
        _readerCounts[flag]++;
        _readerDurationSumPs[flag] += sample.DurationPs;
        _readerDurationMaxPs[flag] = Math.Max(_readerDurationMaxPs[flag], sample.DurationPs);
        _readerTimes[flag] = Math.Max(_readerTimes[flag], sample.Time);
        _readerActiveFlags[flag / 64] |= 1UL << (flag % 64);
    }

    private async Task EmitReaderAggregatesAsync()
    {
        for (var wordIndex = 0; wordIndex < _readerActiveFlags.Length; wordIndex++)
        {
            var activeFlags = _readerActiveFlags[wordIndex];
            _readerActiveFlags[wordIndex] = 0;
            while (activeFlags != 0)
            {
                var bitIndex = BitOperations.TrailingZeroCount(activeFlags);
                var flag = (wordIndex * 64) + bitIndex;
                activeFlags &= activeFlags - 1;

                var value = new ContentionEventStatistics(
                    _readerTimes[flag],
                    (byte)flag,
                    _readerCounts[flag],
                    _readerDurationSumPs[flag] / PicosecondsPerNanosecond,
                    _readerDurationMaxPs[flag] / PicosecondsPerNanosecond);
                _readerCounts[flag] = 0;
                _readerDurationSumPs[flag] = 0;
                _readerDurationMaxPs[flag] = 0;
                _readerTimes[flag] = 0;
                await EmitAsync(value);
            }
        }
    }

    private async Task EmitAsync(ContentionEventStatistics value)
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
                    while (true)
                    {
                        var batchCount = 0;
                        var generation = -1L;
                        while (batchCount < MaxBatchSize && _events.TryDequeue(out var sample))
                        {
                            if (generation >= 0 && sample.Generation != generation)
                            {
                                await EmitReaderAggregatesAsync();
                                batchCount = 0;
                            }
                            generation = sample.Generation;
                            AggregateOnReader(in sample);
                            batchCount++;
                        }

                        if (batchCount != 0)
                        {
                            await EmitReaderAggregatesAsync();
                        }

                        if (batchCount == MaxBatchSize)
                        {
                            continue;
                        }

                        Volatile.Write(ref _notificationPending, 0);
                        if (_events.HasReadyItem && Interlocked.Exchange(ref _notificationPending, 1) == 0)
                        {
                            continue;
                        }
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private readonly record struct ContentionSample(long Time, long Generation, long DurationPs, byte Flag);

    /// <summary>
    /// Fixed-capacity MPSC queue. Producer reservation has a strict retry bound and never waits;
    /// exhaustion is reported as a dropped event by the listener.
    /// </summary>
    private sealed class BoundedMpscQueue<T> where T : struct
    {
        private readonly Slot[] _slots;
        private readonly int _mask;
        private long _enqueuePosition;
        private long _dequeuePosition;

        public BoundedMpscQueue(int capacity)
        {
            if (capacity < 2 || !BitOperations.IsPow2(capacity))
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be a power of two.");
            }

            _slots = new Slot[capacity];
            _mask = capacity - 1;
            for (var i = 0; i < capacity; i++)
            {
                _slots[i].Sequence = i;
            }
        }

        public bool HasReadyItem
        {
            get
            {
                var position = _dequeuePosition;
                return Volatile.Read(ref _slots[(int)position & _mask].Sequence) == position + 1;
            }
        }

        public bool TryEnqueue(T item)
        {
            for (var attempt = 0; attempt < MaxEnqueueAttempts; attempt++)
            {
                var position = Volatile.Read(ref _enqueuePosition);
                ref var slot = ref _slots[(int)position & _mask];
                var difference = Volatile.Read(ref slot.Sequence) - position;
                if (difference < 0)
                {
                    return false;
                }
                if (difference == 0 &&
                    Interlocked.CompareExchange(ref _enqueuePosition, position + 1, position) == position)
                {
                    slot.Item = item;
                    Volatile.Write(ref slot.Sequence, position + 1);
                    return true;
                }
            }

            return false;
        }

        public bool TryDequeue(out T item)
        {
            var position = _dequeuePosition;
            ref var slot = ref _slots[(int)position & _mask];
            if (Volatile.Read(ref slot.Sequence) != position + 1)
            {
                item = default;
                return false;
            }

            item = slot.Item;
            slot.Item = default;
            Volatile.Write(ref slot.Sequence, position + _slots.Length);
            _dequeuePosition = position + 1;
            return true;
        }

        private struct Slot
        {
            public long Sequence;
            public T Item;
        }
    }
}
