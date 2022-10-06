namespace ClrProfiler.TimerListeners;

public abstract class TimerListenerBase
{
    public bool Enabled { get; protected set; }
    protected Action? _eventWritten;

    /// <summary>
    /// Start listener, register handler and run body after registration.
    /// </summary>
    /// <param name="handler"></param>
    /// <param name="body"></param>
    public void RunWithCallback(Action handler, Action body)
    {
        Enabled = true;
        _eventWritten = handler;
        OnEventWritten();
        body();
    }
    /// <summary>
    /// Start listener, register handler and run body after registration.
    /// </summary>
    /// <param name="handler"></param>
    /// <param name="body"></param>
    /// <returns></returns>
    public async Task RunWithCallbackAsync(Action handler, Func<Task> body)
    {
        Enabled = true;
        _eventWritten = handler;
        OnEventWritten();
        await body().ConfigureAwait(false);
    }

    /// <summary>
    /// Restart listner
    /// </summary>
    public virtual void Restart()
    {
        Enabled = true;
    }
    /// <summary>
    /// Stop listner
    /// </summary>
    public virtual void Stop()
    {
        Enabled = false;
    }

    protected abstract void OnEventWritten();
    public abstract void EventCreatedHandler();
}
