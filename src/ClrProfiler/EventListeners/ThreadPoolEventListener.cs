using ClrProfiler.Statistics;
using System.Diagnostics.Tracing;
using System.Threading.Channels;

namespace ClrProfiler.EventListeners;

/// <summary>
/// EventListener to collect Thread events. <see cref="ThreadInfoStatistics"/>.
/// </summary>
/// <remarks>payload: https://docs.microsoft.com/en-us/dotnet/framework/performance/thread-pool-etw-events </remarks>
public class ThreadPoolEventListener : ProfileEventListenerBase, IChannelReader
{
    private readonly Channel<ThreadPoolEventStatistics> _channel;
    private readonly Func<ThreadPoolEventStatistics, Task> _onEventEmit;
    private readonly Action<Exception> _onEventError;

    public ThreadPoolEventListener(Func<ThreadPoolEventStatistics, Task> onEventEmit, Action<Exception> onEventError) : base("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, ClrRuntimeEventKeywords.Threading)
    {
        _onEventEmit = onEventEmit;
        _onEventError = onEventError;
        var channelOption = new BoundedChannelOptions(50)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        };
        _channel = Channel.CreateBounded<ThreadPoolEventStatistics>(channelOption);
    }

    public override void EventCreatedHandler(EventWrittenEventArgs eventData)
    {
        ProcessEvent(eventData.EventName, eventData.TimeStamp, eventData.Payload);
    }

    internal void ProcessEvent(string? eventName, DateTime timeStamp, IReadOnlyList<object?>? payload)
    {
        // ThreadPoolWorkerThreadAdjustmentAdjustment : ThreadPool starvation on Reason 6
        // IOThreadXxxx_ : Windows only.
        if (eventName?.Equals("ThreadPoolWorkerThreadWait", StringComparison.OrdinalIgnoreCase) ?? false) return;

        try
        {
            if (eventName?.Equals("ThreadPoolWorkerThreadAdjustmentAdjustment", StringComparison.OrdinalIgnoreCase) ?? false)
            {
                // do not track on "climbing up" reason.
                var reason = ReadRequiredUInt32(payload, 2);
                if (reason == 3) return;

                long time = timeStamp.Ticks;
                var averageThroughput = ReadRequiredDouble(payload, 0);
                var newWorkerThreadCount = ReadRequiredUInt32(payload, 1);
                var stat = new ThreadPoolEventStatistics(ThreadPoolStatisticType.ThreadPoolAdjustment, new(), new ThreadPoolAdjustmentStatistics(time, averageThroughput, newWorkerThreadCount, reason));

                // write to channel
                _channel.Writer.TryWrite(stat);
            }
            else if (eventName?.StartsWith("ThreadPoolWorkerThreadStop", StringComparison.OrdinalIgnoreCase) ?? false)
            {
                long time = timeStamp.Ticks;
                var activeWorkerThreadCount = ReadRequiredUInt32(payload, 0);
                // always 0
                var stat = new ThreadPoolEventStatistics(ThreadPoolStatisticType.ThreadPoolWorkerStartStop, new ThreadPoolWorkerStatistics(time, activeWorkerThreadCount), new());

                // write to channel
                _channel.Writer.TryWrite(stat);
            }
        }
        catch (Exception ex)
        {
            _onEventError?.Invoke(ex);
        }
    }

    private static uint ReadRequiredUInt32(IReadOnlyList<object?>? payload, int index)
    {
        if (payload is null || (uint)index >= (uint)payload.Count || payload[index] is null)
        {
            throw new InvalidDataException($"Required ThreadPool payload at index {index} is missing.");
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
            _ => throw new InvalidDataException($"ThreadPool payload at index {index} is not an integer."),
        };
    }

    private static double ReadRequiredDouble(IReadOnlyList<object?>? payload, int index)
    {
        if (payload is null || (uint)index >= (uint)payload.Count || payload[index] is null)
        {
            throw new InvalidDataException($"Required ThreadPool payload at index {index} is missing.");
        }

        return payload[index] switch
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
            _ => throw new InvalidDataException($"ThreadPool payload at index {index} is not numeric."),
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
