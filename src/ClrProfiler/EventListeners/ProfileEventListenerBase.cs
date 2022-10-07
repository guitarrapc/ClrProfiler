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
    private List<EventSource>? _tmpEventSourceList = new List<EventSource>();
    private List<EventSource> _enabledEventSourceList = new List<EventSource>();

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
                EnableSourceIfMatch(source, _keywords);
            }
        }
    }
    private void EnableSourceIfMatch(EventSource source, EventKeywords keywords)
    {
        if (source.Name.Equals(_targetSourceName) ||
            source.Guid.Equals(_targetSourceGuid))
        {
            EnableEvents(source, _level, keywords);
            _enabledEventSourceList.Add(source);
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

        EnableSourceIfMatch(eventSource, _keywords);
    }
    // Called whenever an event is written.
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        base.OnEventWritten(eventData);
        _eventWritten?.Invoke(eventData);
    }

    /// <summary>
    /// Start listener, register handler and run body after registration.
    /// </summary>
    /// <param name="handler"></param>
    /// <param name="body"></param>
    public void RunWithCallback(Action<EventWrittenEventArgs> handler, Action body)
    {
        Enabled = true;
        _eventWritten = handler;
        body();
    }
    /// <summary>
    /// Start listener, register handler and run body after registration.
    /// </summary>
    /// <param name="handler"></param>
    /// <param name="body"></param>
    /// <returns></returns>
    public async Task RunWithCallbackAsync(Action<EventWrittenEventArgs> handler, Func<Task> body)
    {
        Enabled = true;
        _eventWritten = handler;
        await body().ConfigureAwait(false);
    }

    /// <summary>
    /// Restart listner
    /// </summary>
    public virtual void Restart()
    {
        Enabled = true;
        foreach (var enabled in _enabledEventSourceList)
        {
            EnableEvents(enabled, EventLevel.Informational, _keywords);
        }
    }
    /// <summary>
    /// Stop listner
    /// </summary>
    public virtual void Stop()
    {
        Enabled = false;
        foreach (var enabled in _enabledEventSourceList)
        {
            DisableEvents(enabled);
        }
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
                var payloadString = payload[i] != null ? payload[i]?.ToString() : string.Empty;
                Console.WriteLine($"    Name = \"{payloadNames[i]}\" Value = \"{payloadString}\"");
            }
            Console.WriteLine("\n");
        }
    }

    public abstract void EventCreatedHandler(EventWrittenEventArgs eventData);
}
