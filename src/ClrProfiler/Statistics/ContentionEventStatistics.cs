using System.Diagnostics.CodeAnalysis;

namespace ClrProfiler.Statistics;

public readonly struct ContentionEventStatistics : IEquatable<ContentionEventStatistics>
{
    public readonly long Time;
    /// <summary>
    /// see - https://learn.microsoft.com/en-us/dotnet/framework/performance/contention-etw-events
    /// 
    /// 0 : managed.
    /// 1 : native
    /// </summary>
    public readonly byte Flag;
    public readonly double DurationNs;

    public ContentionEventStatistics(long time, byte flag, double durationNs)
    {
        Time = time;
        Flag = flag;
        DurationNs = durationNs;
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
            && DurationNs == other.DurationNs;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Time, Flag, DurationNs);
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
