using ClrProfiler.Statistics;
using System.Diagnostics.Tracing;
using System.Globalization;

namespace ClrProfiler.EventListeners;

/// <summary>
/// EventListener to collect Thread events. <see cref="ThreadInfoStatistics"/>.
/// </summary>
/// <remarks>payload: https://docs.microsoft.com/en-us/dotnet/framework/performance/thread-pool-etw-events </remarks>
public class ThreadPoolEventListener : ProfileEventListenerBase, IChannelReader
{
    private readonly BoundedChannelDispatcher<ThreadPoolEventStatistics> _dispatcher;
    private readonly Action<Exception> _onEventError;

    /// <summary>
    /// Gets the cumulative number of events evicted from the bounded delivery channel, plus any
    /// event delivered before a handler was registered.
    /// </summary>
    public long DroppedEventCount => _dispatcher.DroppedEventCount + UnobservedEventCount;

    public ThreadPoolEventListener(Func<ThreadPoolEventStatistics, Task> onEventEmit, Action<Exception> onEventError) : base("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, ClrRuntimeEventKeywords.Threading)
    {
        _onEventError = onEventError;
        // EventListener callbacks run on the threads that emit the runtime events, so multiple
        // application and ThreadPool threads can write concurrently.
        _dispatcher = new BoundedChannelDispatcher<ThreadPoolEventStatistics>(50, singleWriter: false, onEventEmit, onEventError);
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
                _dispatcher.TryWrite(stat);
            }
            else if ((eventName?.StartsWith("ThreadPoolWorkerThreadStop", StringComparison.OrdinalIgnoreCase) ?? false)
                || (eventName?.StartsWith("ThreadPoolWorkerThreadStart", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                // Start and Stop carry the same ActiveWorkerThreadCount payload; tracking both
                // gives the worker count timeline its increase points, not only its decreases.
                long time = timeStamp.Ticks;
                var activeWorkerThreadCount = ReadRequiredUInt32(payload, 0);
                var stat = new ThreadPoolEventStatistics(ThreadPoolStatisticType.ThreadPoolWorkerStartStop, new ThreadPoolWorkerStatistics(time, activeWorkerThreadCount), new());

                // write to channel
                _dispatcher.TryWrite(stat);
            }
        }
        catch (Exception ex)
        {
            ProfilerCallbacks.ReportError(_onEventError, ex);
        }
    }

    private static uint ReadRequiredUInt32(IReadOnlyList<object?>? payload, int index)
    {
        if (payload is null || (uint)index >= (uint)payload.Count)
        {
            throw new InvalidDataException($"Required ThreadPool payload at index {index} is missing.");
        }

        var payloadValue = payload[index];
        if (payloadValue is null)
        {
            throw new InvalidDataException($"Required ThreadPool payload at index {index} is missing.");
        }

        return payloadValue switch
        {
            uint value => value,
            int value => checked((uint)value),
            ushort value => value,
            short value => checked((uint)value),
            byte value => value,
            sbyte value => checked((uint)value),
            ulong value => checked((uint)value),
            long value => checked((uint)value),
            string value => uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException($"ThreadPool payload at index {index} is not an integer."),
        };
    }

    private static double ReadRequiredDouble(IReadOnlyList<object?>? payload, int index)
    {
        if (payload is null || (uint)index >= (uint)payload.Count)
        {
            throw new InvalidDataException($"Required ThreadPool payload at index {index} is missing.");
        }

        var payloadValue = payload[index];
        if (payloadValue is null)
        {
            throw new InvalidDataException($"Required ThreadPool payload at index {index} is missing.");
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
            _ => throw new InvalidDataException($"ThreadPool payload at index {index} is not numeric."),
        };
    }

    public ValueTask OnReadResultAsync(CancellationToken cancellationToken = default) => _dispatcher.ReadAllAsync(cancellationToken);
}
