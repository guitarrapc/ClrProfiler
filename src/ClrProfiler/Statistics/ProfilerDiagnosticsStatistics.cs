using System.Diagnostics.CodeAnalysis;

namespace ClrProfiler.Statistics;

/// <summary>
/// Data structure representing how much data one profiler has discarded.
/// </summary>
/// <remarks>
/// <see cref="DroppedEventCount"/> is cumulative for the lifetime of the profiler, so an adapter can
/// project it as a gauge and the consumer derives the per-interval loss. That also makes the value
/// self-healing when the delivery channel evicts a sample: the newest sample still carries the true
/// total, so a stalled reader delays the metric instead of corrupting it.
/// </remarks>
public readonly struct ProfilerDiagnosticsStatistics(DateTime date, string profilerName, long droppedEventCount) : IEquatable<ProfilerDiagnosticsStatistics>
{
    public readonly DateTime Date = date;
    /// <summary>
    /// <see cref="IProfiler.Name"/> of the profiler this sample describes.
    /// </summary>
    public readonly string ProfilerName = profilerName;
    /// <summary>
    /// Cumulative number of events the profiler discarded because its bounded delivery state was
    /// full. A value that keeps rising means the reader cannot keep up with the runtime.
    /// </summary>
    public readonly long DroppedEventCount = droppedEventCount;

    public override bool Equals(object? obj)
    {
        return obj is ProfilerDiagnosticsStatistics other
            && Equals(other);
    }

    public bool Equals([AllowNull] ProfilerDiagnosticsStatistics other)
    {
        return Date == other.Date
            && string.Equals(ProfilerName, other.ProfilerName, StringComparison.Ordinal)
            && DroppedEventCount == other.DroppedEventCount;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Date, ProfilerName, DroppedEventCount);
    }

    public static bool operator ==(ProfilerDiagnosticsStatistics left, ProfilerDiagnosticsStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ProfilerDiagnosticsStatistics left, ProfilerDiagnosticsStatistics right)
    {
        return !(left == right);
    }
}
