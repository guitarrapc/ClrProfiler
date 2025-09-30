namespace ClrProfiler.DatadogTracing;

public record ClrTrackerOptions
{
    public static ClrTrackerOptions Default => new()
    {
        TrackerType = ClrTrackerType.Datadog,
    };

    /// <summary>
    /// Select the type of ClrTracker to use. If Custom is selected, CustomHandler must be set.
    /// </summary>
    public required ClrTrackerType TrackerType { get; init; }
    /// <summary>
    /// ClrTrackerHandler for Custom tracker type. Must be set if TrackerType is Custom.
    /// </summary>
    public IClrTrackerCallbackHandler? CustomHandler { get; init; }
}

public enum ClrTrackerType
{
    Datadog,
    Logger,
    Custom,
}
