using System.Diagnostics.CodeAnalysis;

namespace ClrProfiler.Statistics;

/// <summary>
/// Contention events aggregated per contention flag over one delivery window.
/// </summary>
/// <remarks>
/// A single value can represent many events, so the duration is exposed as a sum and a maximum
/// rather than as one sample. A lone sample would be meaningless next to <see cref="Count"/>:
/// during a burst the vast majority of samples are never observed by the reader.
/// </remarks>
public readonly struct ContentionEventStatistics : IEquatable<ContentionEventStatistics>
{
    /// <summary>
    /// Timestamp of the newest contention event observed for this flag. The duration fields
    /// summarize the whole window and are not tied to the event this timestamp came from.
    /// </summary>
    public readonly long Time;
    /// <summary>
    /// see - https://learn.microsoft.com/en-us/dotnet/framework/performance/contention-etw-events
    /// 0 : managed.
    /// 1 : native
    /// </summary>
    public readonly byte Flag;
    /// <summary>Number of contention events represented by this value.</summary>
    public readonly long Count;
    /// <summary>Total contention duration in nanoseconds across the represented events.</summary>
    public readonly double DurationNsSum;
    /// <summary>Longest single contention duration in nanoseconds across the represented events.</summary>
    public readonly double DurationNsMax;

    /// <summary>Mean contention duration in nanoseconds, or 0 when nothing was aggregated.</summary>
    public double DurationNsMean => Count == 0 ? 0D : DurationNsSum / Count;

    /// <summary>Creates a value representing a single contention event.</summary>
    public ContentionEventStatistics(long time, byte flag, double durationNs)
        : this(time, flag, 1, durationNs, durationNs)
    {
    }

    public ContentionEventStatistics(long time, byte flag, long count, double durationNsSum, double durationNsMax)
    {
        Time = time;
        Flag = flag;
        Count = count;
        DurationNsSum = durationNsSum;
        DurationNsMax = durationNsMax;
    }

    public override bool Equals(object? obj)
    {
        return obj is ContentionEventStatistics other
            && Equals(other);
    }

    public bool Equals([AllowNull] ContentionEventStatistics other)
    {
        return Time == other.Time
            && Flag == other.Flag
            && Count == other.Count
            && DurationNsSum == other.DurationNsSum
            && DurationNsMax == other.DurationNsMax;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Time, Flag, Count, DurationNsSum, DurationNsMax);
    }

    public static bool operator ==(ContentionEventStatistics left, ContentionEventStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ContentionEventStatistics left, ContentionEventStatistics right)
    {
        return !(left == right);
    }
}
