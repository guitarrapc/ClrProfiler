using System.Diagnostics.CodeAnalysis;

namespace ClrProfiler.Statistics;

public enum GCEventType
{
    GCStartEnd,
    GCSuspend,
}

/// <summary>
/// Data structure represent GC statistics
/// </summary>
public struct GCEventStatistics : IEquatable<GCEventStatistics>
{
    public GCEventType Type { get; set; }
    public GCStartEndStatistics GCStartEndStatistics { get; set; }
    public GCSuspendStatistics GCSuspendStatistics { get; set; }

    public override bool Equals(object? obj)
    {
        return obj is GCEventStatistics other
            && Equals(other);
    }

    public bool Equals([AllowNull] GCEventStatistics other)
    {
        return Type == other.Type
            && GCStartEndStatistics.Equals(other.GCStartEndStatistics)
            && GCSuspendStatistics.Equals(other.GCSuspendStatistics);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, GCStartEndStatistics, GCSuspendStatistics);
    }

    public static bool operator ==(GCEventStatistics left, GCEventStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GCEventStatistics left, GCEventStatistics right)
    {
        return !(left == right);
    }
}

public struct GCStartEndStatistics : IEquatable<GCStartEndStatistics>
{
    public uint Index { get; set; }
    /// <summary>
    /// 0x0 - Blocking garbage collection occurred outside background garbage collection.
    /// 0x1 - Background garbage collection.
    /// 0x2 - Blocking garbage collection occurred during background garbage collection.
    /// </summary>
    public uint Type { get; set; }
    /// <summary>
    /// Gen0-2
    /// </summary>
    public uint Generation { get; set; }
    /// <summary>
    /// 0x0 - Small object heap allocation.
    /// 0x1 - Induced.
    /// 0x2 - Low memory.
    /// 0x3 - Empty.
    /// 0x4 - Large object heap allocation.
    /// 0x5 - Out of space (for small object heap).
    /// 0x6 - Out of space(for large object heap).
    /// 0x7 - Induced but not forced as blocking.
    /// </summary>
    public uint Reason { get; set; }
    public double DurationMillsec { get; set; }
    public long GCStartTime { get; set; }
    public long GCEndTime { get; set; }

    public string GetReasonString()
    {
        return Reason switch
        {
            0 => "soh",
            1 => "induced",
            2 => "low_memory",
            3 => "empty",
            4 => "loh",
            5 => "oos_soh",
            6 => "oos_loh",
            7 => "incuded_non_forceblock",
            _ => throw new ArgumentOutOfRangeException("reason not defined."),
        };
    }

    public override bool Equals(object? obj)
    {
        return obj is GCStartEndStatistics other
            && Equals(other);
    }

    public bool Equals([AllowNull] GCStartEndStatistics other)
    {
        return Index == other.Index &&
            Type == other.Type &&
            Generation == other.Generation &&
            Reason == other.Reason &&
            DurationMillsec == other.DurationMillsec &&
            GCStartTime == other.GCStartTime &&
            GCEndTime == other.GCEndTime;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Index, Type, Generation, Reason, DurationMillsec, GCStartTime, GCEndTime);
    }

    public static bool operator ==(GCStartEndStatistics left, GCStartEndStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GCStartEndStatistics left, GCStartEndStatistics right)
    {
        return !(left == right);
    }
}

public struct GCSuspendStatistics : IEquatable<GCSuspendStatistics>
{
    public double DurationMillisec { get; set; }
    /// <summary>
    /// 0x0 - Other.
    /// 0x1 - Garbage collection.
    /// 0x2 - Application domain shutdown.
    /// 0x3 - Code pitching.
    /// 0x4 - Shutdown.
    /// 0x5 - Debugger.
    /// 0x6 - Preparation for garbage collection.
    /// </summary>
    public uint Reason { get; set; }
    public uint Count { get; set; }

    public string GetReasonString()
    {
        return Reason switch
        {
            0 => "other",
            1 => "gc",
            2 => "appdomain_shudown",
            3 => "code_pitch",
            4 => "shutdown",
            5 => "debugger",
            6 => "prep_gc",
            _ => throw new ArgumentOutOfRangeException("reason not defined."),
        };
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(DurationMillisec, Reason, Count);
    }

    public bool Equals([AllowNull] GCSuspendStatistics other)
    {
        return DurationMillisec == other.DurationMillisec &&
               Reason == other.Reason &&
               Count == other.Count;
    }

    public override bool Equals(object? obj)
    {
        return obj is GCSuspendStatistics statistics && Equals(statistics);
    }

    public static bool operator ==(GCSuspendStatistics left, GCSuspendStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GCSuspendStatistics left, GCSuspendStatistics right)
    {
        return !(left == right);
    }
}
