namespace ClrProfiler.EventListeners;

public interface IChannelReader
{
    ValueTask OnReadResultAsync(CancellationToken cancellationToken);
}
