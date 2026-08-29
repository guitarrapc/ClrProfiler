namespace ClrProfiler;

/// <summary>
/// CLR instrumentation that can be enabled by <see cref="ProfilerTracker"/>.
/// </summary>
[Flags]
public enum ProfilerFeature
{
    None = 0,
    GCEvent = 1 << 0,
    ThreadPoolEvent = 1 << 1,
    ContentionEvent = 1 << 2,
    ThreadInfoTimer = 1 << 3,
    GCInfoTimer = 1 << 4,
    ProcessInfoTimer = 1 << 5,
    /// <summary>
    /// Periodically reports <see cref="IProfiler.DroppedEventCount"/> for every profiler the tracker
    /// owns, so data the profiler discarded is visible instead of silently missing.
    /// </summary>
    ProfilerDiagnosticsTimer = 1 << 6,
    All = GCEvent | ThreadPoolEvent | ContentionEvent | ThreadInfoTimer | GCInfoTimer | ProcessInfoTimer | ProfilerDiagnosticsTimer,
}
