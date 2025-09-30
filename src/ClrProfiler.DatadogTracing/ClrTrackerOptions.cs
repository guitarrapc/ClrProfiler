namespace ClrProfiler.DatadogTracing;

public record ClrTrackerOptions
{
    public static ClrTrackerOptions Default => new()
    {
        TrackerType = ClrTrackerType.Datadog,
    };

    public required ClrTrackerType TrackerType { get; init; }
}

public enum ClrTrackerType
{
    Datadog,
    Logger,
}
