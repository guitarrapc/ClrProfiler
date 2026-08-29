using ClrProfiler.EventListeners;
using System.Diagnostics.Tracing;

namespace ClrProfiler.UnitTest;

/// <summary>
/// Subscription lifecycle of <see cref="ProfileEventListenerBase"/> against a deterministic
/// in-process <see cref="EventSource"/>: no event flows before Start, Stop/Restart toggle the
/// subscription at the construction level, and an event delivered without a handler is counted
/// instead of silently discarded.
/// </summary>
[NotInParallel]
public class EventListenerLifecycleTest
{
    [Test]
    public async Task ListenerDoesNotSubscribeSourceUntilStart()
    {
        var source = LifecycleTestEventSource.Log;
        using var listener = new RecordingListener(source.Name, EventLevel.Informational);

        // Construction must not enable the source; otherwise events between construction and
        // Start are dispatched into a listener with no handler and lost without a trace.
        await Assert.That(source.IsEnabled()).IsFalse();
        source.Info();

        listener.Start();
        await Assert.That(source.IsEnabled()).IsTrue();
        source.Info();

        await Assert.That(listener.EventNames).Count().IsEqualTo(1);
        await Assert.That(listener.UnobservedEventCount).IsEqualTo(0L);
    }

    [Test]
    public async Task StopUnsubscribesAndRestartResubscribesAtConstructionLevel()
    {
        var source = LifecycleTestEventSource.Log;
        using var listener = new RecordingListener(source.Name, EventLevel.Verbose);

        listener.Start();
        source.Verbose();
        await Assert.That(listener.EventNames).Count().IsEqualTo(1);

        listener.Stop();
        await Assert.That(source.IsEnabled()).IsFalse();
        source.Verbose();
        await Assert.That(listener.EventNames).Count().IsEqualTo(1);

        // Restart must re-enable with the level given at construction, not a hardcoded
        // Informational, so a Verbose listener keeps receiving Verbose events.
        listener.Restart();
        source.Verbose();
        await Assert.That(listener.EventNames).Count().IsEqualTo(2);
    }

    [Test]
    public async Task EventDeliveredWithoutHandlerIsCountedNotSilentlyDiscarded()
    {
        var source = LifecycleTestEventSource.Log;
        using var listener = new RecordingListener(source.Name, EventLevel.Informational);

        // Restart without a prior Start enables the source while no handler is registered.
        listener.Restart();
        await Assert.That(source.IsEnabled()).IsTrue();
        source.Info();

        await Assert.That(listener.UnobservedEventCount).IsEqualTo(1L);
        await Assert.That(listener.EventNames).IsEmpty();
    }

    [Test]
    public async Task SourceCreatedAfterStartIsSubscribedImmediately()
    {
        using var listener = new RecordingListener(LateLifecycleTestEventSource.SourceName, EventLevel.Informational);
        listener.Start();

        using var late = new LateLifecycleTestEventSource();
        await Assert.That(late.IsEnabled()).IsTrue();
        late.Info();

        await Assert.That(listener.EventNames).Count().IsEqualTo(1);
    }

    private sealed class RecordingListener(string sourceName, EventLevel level)
        : ProfileEventListenerBase(sourceName, level, -1L)
    {
        private readonly List<string> _eventNames = [];

        public IReadOnlyList<string> EventNames
        {
            get
            {
                lock (_eventNames)
                {
                    return [.. _eventNames];
                }
            }
        }

        public override void EventCreatedHandler(EventWrittenEventArgs eventData)
        {
            lock (_eventNames)
            {
                _eventNames.Add(eventData.EventName ?? string.Empty);
            }
        }

        public void Start() => RunWithCallback(EventCreatedHandler, static () => { });
    }

    [EventSource(Name = "ClrProfiler-UnitTest-Lifecycle")]
    private sealed class LifecycleTestEventSource : EventSource
    {
        public static readonly LifecycleTestEventSource Log = new();

        [Event(1, Level = EventLevel.Informational)]
        public void Info() => WriteEvent(1);

        [Event(2, Level = EventLevel.Verbose)]
        public void Verbose() => WriteEvent(2);
    }

    [EventSource(Name = SourceName)]
    private sealed class LateLifecycleTestEventSource : EventSource
    {
        public const string SourceName = "ClrProfiler-UnitTest-LateLifecycle";

        [Event(1, Level = EventLevel.Informational)]
        public void Info() => WriteEvent(1);
    }
}
