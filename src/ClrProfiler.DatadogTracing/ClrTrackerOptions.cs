namespace ClrProfiler.DatadogTracing;

public record ClrTrackerOptions
{
    public static ClrTrackerOptions Default => new()
    {
        TrackerType = ClrTrackerType.Datadog,
    };

    /// <summary>
    /// Selects the CLR instrumentation to create and run. Defaults to all features for compatibility.
    /// </summary>
    public ProfilerFeature EnabledFeatures { get; init; } = ProfilerFeature.All;
    /// <summary>
    /// Gets additional profiler factories whose instances are managed and disposed by the tracker.
    /// </summary>
    public IReadOnlyList<Func<IProfiler>> AdditionalProfilerFactories { get; init; } = Array.Empty<Func<IProfiler>>();
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
