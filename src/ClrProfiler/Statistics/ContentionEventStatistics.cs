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
    /// <summary>Number of completed contention events (ContentionStop) represented by this value.</summary>
    public readonly long Count;
    /// <summary>
    /// Number of contention begins (ContentionStart) observed in this window. A window whose
    /// starts exceed its completions over time indicates threads that are still blocked; a
    /// deadlock produces windows with starts and no completions instead of silence.
    /// </summary>
    public readonly long StartCount;
    /// <summary>Total contention duration in nanoseconds across the represented events.</summary>
    public readonly double DurationNsSum;
    /// <summary>Longest single contention duration in nanoseconds across the represented events.</summary>
    public readonly double DurationNsMax;

    /// <summary>Mean contention duration in nanoseconds, or 0 when nothing was aggregated.</summary>
    public double DurationNsMean => Count == 0 ? 0D : DurationNsSum / Count;

    /// <summary>Creates a value representing a single completed contention event.</summary>
    public ContentionEventStatistics(long time, byte flag, double durationNs)
        : this(time, flag, 1, 0, durationNs, durationNs)
    {
    }

    /// <summary>Creates a window of completed contention events with no observed begins.</summary>
    public ContentionEventStatistics(long time, byte flag, long count, double durationNsSum, double durationNsMax)
        : this(time, flag, count, 0, durationNsSum, durationNsMax)
    {
    }

    public ContentionEventStatistics(long time, byte flag, long count, long startCount, double durationNsSum, double durationNsMax)
    {
        Time = time;
        Flag = flag;
        Count = count;
        StartCount = startCount;
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
            && StartCount == other.StartCount
            && DurationNsSum == other.DurationNsSum
            && DurationNsMax == other.DurationNsMax;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Time, Flag, Count, StartCount, DurationNsSum, DurationNsMax);
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
