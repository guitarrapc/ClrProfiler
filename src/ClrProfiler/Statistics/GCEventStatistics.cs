using System.Diagnostics.CodeAnalysis;

namespace ClrProfiler.Statistics;

public enum GCEventType
{
    GCStartEnd,
    GCSuspend,
    GCHeapStats,
    GCGlobalHistory,
}

/// <summary>
/// Data structure represent GC statistics
/// </summary>
public readonly struct GCEventStatistics : IEquatable<GCEventStatistics>
{
    public readonly GCEventType Type;
    public readonly GCStartEndStatistics GCStartEndStatistics;
    public readonly GCSuspendStatistics GCSuspendStatistics;
    public readonly GCHeapStatistics GCHeapStatistics;
    public readonly GCGlobalHistoryStatistics GCGlobalHistoryStatistics;

    public GCEventStatistics(GCEventType type, GCStartEndStatistics gCStartEndStatistics, GCSuspendStatistics gCSuspendStatistics)
        : this(type, gCStartEndStatistics, gCSuspendStatistics, default, default)
    {
    }

    public GCEventStatistics(GCEventType type, GCStartEndStatistics gCStartEndStatistics, GCSuspendStatistics gCSuspendStatistics, GCHeapStatistics gCHeapStatistics)
        : this(type, gCStartEndStatistics, gCSuspendStatistics, gCHeapStatistics, default)
    {
    }

    public GCEventStatistics(GCEventType type, GCStartEndStatistics gCStartEndStatistics, GCSuspendStatistics gCSuspendStatistics, GCHeapStatistics gCHeapStatistics, GCGlobalHistoryStatistics gCGlobalHistoryStatistics)
    {
        Type = type;
        GCStartEndStatistics = gCStartEndStatistics;
        GCSuspendStatistics = gCSuspendStatistics;
        GCHeapStatistics = gCHeapStatistics;
        GCGlobalHistoryStatistics = gCGlobalHistoryStatistics;
    }

    public override bool Equals(object? obj)
    {
        return obj is GCEventStatistics other
            && Equals(other);
    }

    public bool Equals([AllowNull] GCEventStatistics other)
    {
        return Type == other.Type
            && GCStartEndStatistics.Equals(other.GCStartEndStatistics)
            && GCSuspendStatistics.Equals(other.GCSuspendStatistics)
            && GCHeapStatistics.Equals(other.GCHeapStatistics)
            && GCGlobalHistoryStatistics.Equals(other.GCGlobalHistoryStatistics);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, GCStartEndStatistics, GCSuspendStatistics, GCHeapStatistics, GCGlobalHistoryStatistics);
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

public readonly struct GCStartEndStatistics(uint index, uint type, uint generation, uint reason, double durationMillsec, long gCStartTime, long gCEndTime) : IEquatable<GCStartEndStatistics>
{
    public readonly uint Index = index;
    /// <summary>
    /// 0x0 - Blocking garbage collection occurred outside background garbage collection.
    /// 0x1 - Background garbage collection.
    /// 0x2 - Blocking garbage collection occurred during background garbage collection.
    /// </summary>
    public readonly uint Type = type;
    /// <summary>
    /// Gen0-2
    /// </summary>
    public readonly uint Generation = generation;
    /// <summary>
    /// see - https://learn.microsoft.com/en-us/dotnet/framework/performance/garbage-collection-etw-events#gcstart_v1-event
    /// 
    /// 0x0 - Small object heap allocation.
    /// 0x1 - Induced.
    /// 0x2 - Low memory.
    /// 0x3 - Empty.
    /// 0x4 - Large object heap allocation.
    /// 0x5 - Out of space (for small object heap).
    /// 0x6 - Out of space(for large object heap).
    /// 0x7 - Induced but not forced as blocking.
    /// 0x8 - Stress testing.
    /// 0x9 - The finalizer thread observed the process is in low memory and induced a GC.
    /// 0x10 - User code induced GC and requested it to be a compacting GC.
    /// </summary>
    public readonly uint Reason = reason;
    public readonly double DurationMillsec = durationMillsec;
    public readonly long GCStartTime = gCStartTime;
    public readonly long GCEndTime = gCEndTime;

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
            8 => "stress_testing",
            9 => "finalizer_low_memory_induced",
            10 => "user_gc_request",
            _ => throw new ArgumentOutOfRangeException($"reason not defined. reason: {Reason}"),
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

/// <summary>
/// Heap state at the end of a garbage collection, from the GCHeapStats_V1/V2 event. Sizes are the
/// per-generation sizes after that collection; V1 payloads have no POH, which reports as zero.
/// </summary>
public readonly struct GCHeapStatistics(long time, ulong gen0Size, ulong gen1Size, ulong gen2Size, ulong lohSize, ulong pohSize, ulong finalizationPromotedSize, uint pinnedObjectCount, uint gcHandleCount) : IEquatable<GCHeapStatistics>
{
    public readonly long Time = time;
    /// <summary>
    /// bytes
    /// </summary>
    public readonly ulong Gen0Size = gen0Size;
    /// <summary>
    /// bytes
    /// </summary>
    public readonly ulong Gen1Size = gen1Size;
    /// <summary>
    /// bytes
    /// </summary>
    public readonly ulong Gen2Size = gen2Size;
    /// <summary>
    /// bytes
    /// </summary>
    public readonly ulong LohSize = lohSize;
    /// <summary>
    /// bytes. Zero when the runtime emits GCHeapStats_V1, which predates the pinned object heap.
    /// </summary>
    public readonly ulong PohSize = pohSize;
    /// <summary>
    /// bytes promoted because of finalization in this collection.
    /// </summary>
    public readonly ulong FinalizationPromotedSize = finalizationPromotedSize;
    public readonly uint PinnedObjectCount = pinnedObjectCount;
    public readonly uint GCHandleCount = gcHandleCount;

    public override bool Equals(object? obj)
    {
        return obj is GCHeapStatistics other && Equals(other);
    }

    public bool Equals([AllowNull] GCHeapStatistics other)
    {
        return Time == other.Time &&
            Gen0Size == other.Gen0Size &&
            Gen1Size == other.Gen1Size &&
            Gen2Size == other.Gen2Size &&
            LohSize == other.LohSize &&
            PohSize == other.PohSize &&
            FinalizationPromotedSize == other.FinalizationPromotedSize &&
            PinnedObjectCount == other.PinnedObjectCount &&
            GCHandleCount == other.GCHandleCount;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Time);
        hash.Add(Gen0Size);
        hash.Add(Gen1Size);
        hash.Add(Gen2Size);
        hash.Add(LohSize);
        hash.Add(PohSize);
        hash.Add(FinalizationPromotedSize);
        hash.Add(PinnedObjectCount);
        hash.Add(GCHandleCount);
        return hash.ToHashCode();
    }

    public static bool operator ==(GCHeapStatistics left, GCHeapStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GCHeapStatistics left, GCHeapStatistics right)
    {
        return !(left == right);
    }
}

/// <summary>
/// Per-collection summary from the GCGlobalHeapHistory event: which generation was condemned, why
/// the collection ran, and which global mechanisms (compaction, concurrency) it used.
/// </summary>
public readonly struct GCGlobalHistoryStatistics(long time, uint condemnedGeneration, uint reason, uint globalMechanisms, uint memoryPressure) : IEquatable<GCGlobalHistoryStatistics>
{
    private const uint ConcurrentMechanism = 0x1;
    private const uint CompactionMechanism = 0x2;

    public readonly long Time = time;
    /// <summary>
    /// Generation actually condemned by this collection (0-2). Compare with the requested
    /// generation on GCStart to observe escalation.
    /// </summary>
    public readonly uint CondemnedGeneration = condemnedGeneration;
    /// <summary>
    /// Same value space as <see cref="GCStartEndStatistics.Reason"/>.
    /// </summary>
    public readonly uint Reason = reason;
    /// <summary>
    /// Raw global mechanisms bitmask. Bit 0x1 = concurrent, 0x2 = compaction, 0x4 = promotion,
    /// 0x8 = demotion, 0x10 = card bundles.
    /// </summary>
    public readonly uint GlobalMechanisms = globalMechanisms;
    /// <summary>
    /// Memory load percentage the GC observed (0-100). Zero on runtimes emitting a payload
    /// version that predates the field.
    /// </summary>
    public readonly uint MemoryPressure = memoryPressure;

    /// <summary>True when this collection compacted the heap.</summary>
    public bool Compacting => (GlobalMechanisms & CompactionMechanism) != 0;
    /// <summary>True when this collection ran concurrently (background GC).</summary>
    public bool Concurrent => (GlobalMechanisms & ConcurrentMechanism) != 0;

    public override bool Equals(object? obj)
    {
        return obj is GCGlobalHistoryStatistics other && Equals(other);
    }

    public bool Equals([AllowNull] GCGlobalHistoryStatistics other)
    {
        return Time == other.Time &&
            CondemnedGeneration == other.CondemnedGeneration &&
            Reason == other.Reason &&
            GlobalMechanisms == other.GlobalMechanisms &&
            MemoryPressure == other.MemoryPressure;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Time, CondemnedGeneration, Reason, GlobalMechanisms, MemoryPressure);
    }

    public static bool operator ==(GCGlobalHistoryStatistics left, GCGlobalHistoryStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GCGlobalHistoryStatistics left, GCGlobalHistoryStatistics right)
    {
        return !(left == right);
    }
}

public readonly struct GCSuspendStatistics(double durationMillisec, uint reason, uint count) : IEquatable<GCSuspendStatistics>
{
    public readonly double DurationMillisec = durationMillisec;
    /// <summary>
    /// see - https://learn.microsoft.com/en-us/dotnet/framework/performance/garbage-collection-etw-events#gcsuspendee_v1-event
    /// 
    /// 0x0 - Other.
    /// 0x1 - Garbage collection.
    /// 0x2 - Application domain shutdown.
    /// 0x3 - Code pitching.
    /// 0x4 - Shutdown.
    /// 0x5 - Debugger.
    /// 0x6 - Preparation for garbage collection.
    /// </summary>
    public readonly uint Reason = reason;
    public readonly uint Count = count;

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
            _ => throw new ArgumentOutOfRangeException($"reason not defined. passed reason is {Reason}"),
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
