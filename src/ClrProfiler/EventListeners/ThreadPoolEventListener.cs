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
        // ThreadPoolWorkerThreadAdjustmentAdjustment : ThreadPool starvation on Reason 6
        // IOThreadXxxx_ : Windows only.
        if (eventData.EventName?.Equals("ThreadPoolWorkerThreadWait", StringComparison.OrdinalIgnoreCase) ?? false) return;

        try
        {
            if (eventData.EventName?.Equals("ThreadPoolWorkerThreadAdjustmentAdjustment", StringComparison.OrdinalIgnoreCase) ?? false)
            {
                // do not track on "climing up" reason.
                var r = eventData.Payload?[2]?.ToString() ?? "0";
                if (r == "3") return;

                long time = eventData.TimeStamp.Ticks;
                var averageThroughput = double.Parse(eventData.Payload?[0]?.ToString() ?? "0");
                var newWorkerThreadCount = uint.Parse(eventData.Payload?[1]?.ToString() ?? "0");
                var reason = uint.Parse(r);
                var stat = new ThreadPoolEventStatistics(ThreadPoolStatisticType.ThreadPoolAdjustment, new(), new ThreadPoolAdjustmentStatistics(time, averageThroughput, newWorkerThreadCount, reason));

                // write to channel
                _channel.Writer.TryWrite(stat);
            }
            else if (eventData.EventName?.StartsWith("ThreadPoolWorkerThreadStop", StringComparison.OrdinalIgnoreCase) ?? false)
            {
                long time = eventData.TimeStamp.Ticks;
                var activeWorkerThreadCount = uint.Parse(eventData.Payload?[0]?.ToString() ?? "0");
                // always 0
                // var retiredWrokerThreadCount = uint.Parse(eventData.Payload[1].ToString());
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
