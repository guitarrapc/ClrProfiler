using System.Diagnostics.CodeAnalysis;

namespace ClrProfiler.Statistics;

public readonly struct ContentionEventStatistics : IEquatable<ContentionEventStatistics>
{
    public readonly long Time;
    /// <summary>
    /// see - https://learn.microsoft.com/en-us/dotnet/framework/performance/contention-etw-events
    /// 0 : managed.
    /// 1 : native
    /// </summary>
    public readonly byte Flag;
    /// <summary>Total duration in nanoseconds across the aggregated contention events.</summary>
    public readonly double DurationNs;
    /// <summary>Number of contention events represented by this value.</summary>
    public readonly long Count;
    /// <summary>Average duration in nanoseconds per represented contention event.</summary>
    public double AverageDurationNs => Count > 0 ? DurationNs / Count : 0;

    public ContentionEventStatistics(long time, byte flag, double durationNs)
        : this(time, flag, durationNs, 1)
    {
    }

    public ContentionEventStatistics(long time, byte flag, double durationNs, long count)
    {
        Time = time;
        Flag = flag;
        DurationNs = durationNs;
        Count = count;
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
            && DurationNs == other.DurationNs
            && Count == other.Count;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Time, Flag, DurationNs, Count);
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
