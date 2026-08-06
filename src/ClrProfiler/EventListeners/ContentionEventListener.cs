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

    private readonly Channel<bool> _flushSignal;
    private readonly Func<ContentionEventStatistics, Task> _onEventEmit;
    private readonly Action<Exception> _onEventError;
    private readonly long[] _counts = new long[ContentionFlagCount];
    private readonly long[] _durationBits = new long[ContentionFlagCount];
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
        Interlocked.Exchange(ref _durationBits[flag], BitConverter.DoubleToInt64Bits(durationNs));
        Interlocked.Exchange(ref _times[flag], time);
        if (Interlocked.Increment(ref _counts[flag]) == 1)
        {
            Interlocked.Or(ref _activeFlags[flag / 64], 1L << (flag % 64));
            _flushSignal.Writer.TryWrite(true);
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
                                continue;
                            }
                            var durationBits = Volatile.Read(ref _durationBits[flag]);
                            var time = Volatile.Read(ref _times[flag]);
                            var value = new ContentionEventStatistics(time, (byte)flag, BitConverter.Int64BitsToDouble(durationBits), count);
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
