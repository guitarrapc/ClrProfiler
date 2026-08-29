using System.Diagnostics.Tracing;

namespace ClrProfiler.EventListeners;

public abstract class ProfileEventListenerBase : EventListener
{
    public bool Enabled { get; protected set; }

    private readonly string? _targetSourceName;
    private readonly Guid _targetSourceGuid;
    private readonly EventLevel _level;
    private readonly EventKeywords _keywords;

    private Action<EventWrittenEventArgs>? _eventWritten;
    private List<EventSource>? _tmpEventSourceList = [];
    // Sources matching the target. Recorded at any time, but only enabled while _listening,
    // so no event is dispatched before a handler is registered in RunWithCallback.
    private readonly List<EventSource> _matchedEventSourceList = [];
    private readonly object _matchedSourceLock = new();
    private bool _listening;
    private long _unobservedEventCount;

    /// <summary>
    /// Gets the cumulative number of events delivered to this listener while no handler was
    /// registered. Sources are enabled only after a handler is set, so a non-zero value means
    /// the listener was restarted without ever being started.
    /// </summary>
    public long UnobservedEventCount => Volatile.Read(ref _unobservedEventCount);

    // .ctor call after OnEventSourceCreated. https://github.com/Microsoft/ApplicationInsights-dotnet/issues/1106
    // https://github.com/dotnet/corefx/blob/master/src/Common/tests/System/Diagnostics/Tracing/TestEventListener.cs#L40
    public ProfileEventListenerBase(string targetSourceName, EventLevel level, ClrRuntimeEventKeywords keywords)
    {
        // Store the arguments
        _targetSourceName = targetSourceName;
        _level = level;
        _keywords = (EventKeywords)(long)keywords;

        LoadSourceList();
    }
    public ProfileEventListenerBase(string targetSourceName, EventLevel level, long keywords)
    {
        // Store the arguments
        _targetSourceName = targetSourceName;
        _level = level;
        _keywords = (EventKeywords)keywords;

        LoadSourceList();
    }
    public ProfileEventListenerBase(Guid targetSourceGuid, EventLevel level, ClrRuntimeEventKeywords keywords)
    {
        // Store the arguments
        _targetSourceGuid = targetSourceGuid;
        _level = level;
        _keywords = (EventKeywords)(long)keywords;

        LoadSourceList();
    }
    private void LoadSourceList()
    {
        // The base constructor, which is called before this constructor,
        // will invoke the virtual OnEventSourceCreated method for each
        // existing EventSource, which means OnEventSourceCreated will be
        // called before _targetSourceGuid and _level have been set.  As such,
        // we store a temporary list that just exists from the moment this instance
        // is created (instance field initializers run before the base constructor)
        // and until we finish construction... in that window, OnEventSourceCreated
        // will store the sources into the list rather than try to enable them directly,
        // and then here we can enumerate that list, then clear it out.
        List<EventSource> sources;
        if (_tmpEventSourceList != null)
        {
            lock (_tmpEventSourceList)
            {
                sources = _tmpEventSourceList;
                _tmpEventSourceList = null;
            }
            foreach (EventSource source in sources)
            {
                RecordSourceIfMatch(source);
            }
        }
    }
    private void RecordSourceIfMatch(EventSource source)
    {
        if (source.Name.Equals(_targetSourceName) ||
            source.Guid.Equals(_targetSourceGuid))
        {
            bool listening;
            lock (_matchedSourceLock)
            {
                _matchedEventSourceList.Add(source);
                listening = _listening;
            }
            // Enable outside the lock: EnableEvents takes EventListener's own lock, and holding
            // ours across it would order locks against OnEventSourceCreated callbacks.
            if (listening)
            {
                EnableEvents(source, _level, _keywords);
            }
        }
    }
    private void SetListening(bool listening)
    {
        EventSource[] sources;
        lock (_matchedSourceLock)
        {
            _listening = listening;
            sources = [.. _matchedEventSourceList];
        }
        foreach (var source in sources)
        {
            if (listening)
            {
                EnableEvents(source, _level, _keywords);
            }
            else
            {
                DisableEvents(source);
            }
        }
    }

    // Called whenever an EventSource is created.
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        base.OnEventSourceCreated(eventSource);

        List<EventSource>? tmp = _tmpEventSourceList;
        if (tmp != null)
        {
            lock (tmp)
            {
                if (_tmpEventSourceList != null)
                {
                    _tmpEventSourceList.Add(eventSource);
                    return;
                }
            }
        }

        RecordSourceIfMatch(eventSource);
    }
    // Called whenever an event is written.
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        base.OnEventWritten(eventData);
        var handler = _eventWritten;
        if (handler is null)
        {
            Interlocked.Increment(ref _unobservedEventCount);
            return;
        }
        handler.Invoke(eventData);
    }

    /// <summary>
    /// Start listener, register handler and run body after registration.
    /// Sources are enabled only here, after the handler is registered, so no event can arrive
    /// while there is nothing to observe it.
    /// </summary>
    /// <param name="handler"></param>
    /// <param name="body"></param>
    public void RunWithCallback(Action<EventWrittenEventArgs> handler, Action body)
    {
        _eventWritten = handler;
        Enabled = true;
        SetListening(true);
        body();
    }
    /// <summary>
    /// Start listener, register handler and run body after registration.
    /// Sources are enabled only here, after the handler is registered, so no event can arrive
    /// while there is nothing to observe it.
    /// </summary>
    /// <param name="handler"></param>
    /// <param name="body"></param>
    /// <returns></returns>
    public async Task RunWithCallbackAsync(Action<EventWrittenEventArgs> handler, Func<Task> body)
    {
        _eventWritten = handler;
        Enabled = true;
        SetListening(true);
        await body().ConfigureAwait(false);
    }

    /// <summary>
    /// Restart listner
    /// </summary>
    public virtual void Restart()
    {
        Enabled = true;
        SetListening(true);
    }
    /// <summary>
    /// Stop listner
    /// </summary>
    public virtual void Stop()
    {
        Enabled = false;
        SetListening(false);
    }

    /// <summary>
    /// Debug Listener output handler
    /// </summary>
    /// <param name="eventData"></param>
    public virtual void DebugEventDataDetailHandler(EventWrittenEventArgs eventData)
    {
        Console.WriteLine($"ThreadID = {eventData.OSThreadId} ID = {eventData.EventId} Name = {eventData.EventName}");
        var payload = eventData.Payload;
        var payloadNames = eventData.PayloadNames;
        if (payload != null && payloadNames != null)
        {
            for (int i = 0; i < payload.Count; i++)
            {
                var payloadString = payload[i]?.ToString() ?? string.Empty;
                Console.WriteLine($"    Name = \"{payloadNames[i]}\" Value = \"{payloadString}\"");
            }
            Console.WriteLine("\n");
        }
    }

    public abstract void EventCreatedHandler(EventWrittenEventArgs eventData);
}
