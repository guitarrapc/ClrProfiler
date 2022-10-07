using ClrProfiler.Statistics;
using System.Diagnostics.Tracing;
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
        try
        {
            if (!string.IsNullOrWhiteSpace(eventData.EventName) && eventData.EventName.StartsWith("ContentionStop_", StringComparison.OrdinalIgnoreCase))
            {
                long time = eventData.TimeStamp.Ticks;
                var flag = byte.Parse(eventData.Payload?[0]?.ToString() ?? "0");
                var durationNs = double.Parse(eventData.Payload?[2]?.ToString() ?? "0");
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
