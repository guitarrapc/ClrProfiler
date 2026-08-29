using System.Threading.Channels;

namespace ClrProfiler.EventListeners;

/// <summary>
/// Owns bounded, non-blocking delivery for listeners that retain the newest values when full.
/// </summary>
internal sealed class BoundedChannelDispatcher<T> where T : struct
{
    private readonly Channel<T> _channel;
    private readonly Func<T, Task>? _onEventEmit;
    private readonly Action<Exception> _onEventError;
    private long _droppedEventCount;

    public long DroppedEventCount => Volatile.Read(ref _droppedEventCount);

    public BoundedChannelDispatcher(
        int capacity,
        bool singleWriter,
        Func<T, Task>? onEventEmit,
        Action<Exception> onEventError)
    {
        _onEventEmit = onEventEmit;
        _onEventError = onEventError;
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = singleWriter,
            FullMode = BoundedChannelFullMode.DropOldest,
        }, OnItemDropped);
    }

    public void TryWrite(T value) => _channel.Writer.TryWrite(value);

    public async ValueTask ReadAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_channel.Reader.TryRead(out var value))
                {
                    if (_onEventEmit is null)
                    {
                        continue;
                    }

                    try
                    {
                        await _onEventEmit.Invoke(value);
                    }
                    catch (Exception ex)
                    {
                        // A throwing emit callback must not terminate this reader loop.
                        ProfilerCallbacks.ReportError(_onEventError, ex);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void OnItemDropped(T _) => Interlocked.Increment(ref _droppedEventCount);
}
