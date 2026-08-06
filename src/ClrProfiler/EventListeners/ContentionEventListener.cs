using ClrProfiler.Statistics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Threading.Channels;

namespace ClrProfiler.EventListeners;

/// <summary>
/// Contention events are raised whenever there is contention for System.Threading.Monitor locks or native locks used by the runtime. 
/// Contention occurs when a thread is waiting for a lock while another thread possesses the lock.
/// https://docs.microsoft.com/en-us/dotnet/framework/performance/contention-etw-events
/// </summary>
public class ContentionEventListener : ProfileEventListenerBase, IChannelReader
{
    private readonly Channel<ContentionEventStatistics> _channel;
    private readonly Func<ContentionEventStatistics, Task> _onEventEmit;
    private readonly Action<Exception> _onEventError;

    public ContentionEventListener(Func<ContentionEventStatistics, Task> onEventEmit, Action<Exception> onEventError) : base("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, ClrRuntimeEventKeywords.Contention)
    {
        _onEventEmit = onEventEmit;
        _onEventError = onEventError;
        var channelOption = new BoundedChannelOptions(50)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        };
        _channel = Channel.CreateBounded<ContentionEventStatistics>(channelOption);
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
                var stat = new ContentionEventStatistics(time, flag, durationNs);

                // write to channel
                _channel.Writer.TryWrite(stat);
            }
        }
        catch (Exception ex)
        {
            _onEventError?.Invoke(ex);
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
