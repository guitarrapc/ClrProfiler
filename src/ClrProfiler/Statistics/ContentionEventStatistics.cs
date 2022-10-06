using System.Diagnostics.CodeAnalysis;

namespace ClrProfiler.Statistics;

public struct ContentionEventStatistics : IEquatable<ContentionEventStatistics>
{
    public long Time { get; set; }
    /// <summary>
    /// 0 : managed.
    /// 1 : native
    /// </summary>
    public byte Flag { get; set; }
    public double DurationNs { get; set; }

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
