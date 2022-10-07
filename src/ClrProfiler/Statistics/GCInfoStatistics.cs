using System.Diagnostics.CodeAnalysis;
using System.Runtime;

namespace ClrProfiler.Statistics;

public enum GCMode
{
    Workstation = 0,
    Server = 1,
}
public readonly struct GCInfoStatistics : IEquatable<GCInfoStatistics>
{
    public readonly DateTime Date;
    public readonly GCMode GCMode;
    public readonly GCLargeObjectHeapCompactionMode CompactionMode;
    /// <summary>
    /// <strong>Batch</strong>
    /// For applications that have no user interface (UI) or server-side operations.
    /// When background garbage collection is disabled, this is the default mode for workstation and server garbage collection.Batch mode also overrides the gcConcurrent setting, that is, it prevents background or concurrent collections.
    /// <br/>
    /// <strong>Interactive</strong>
    /// For most applications that have a UI.
    /// This is the default mode for workstation and server garbage collection.However, if an app is hosted, the garbage collector settings of the hosting process take precedence.
    /// <br/>
    /// <strong>LowLatency</strong>
    /// For applications that have short-term, time-sensitive operations during which interruptions from the garbage collector could be disruptive. For example, applications that render animations or data acquisition functions.
    /// <br/>
    /// <strong>SustainedLowLatency</strong>
    /// For applications that have time-sensitive operations for a contained but potentially longer duration of time during which interruptions from the garbage collector could be disruptive. For example, applications that need quick response times as market data changes during trading hours.
    /// This mode results in a larger managed heap size than other modes.Because it does not compact the managed heap, higher fragmentation is possible.Ensure that sufficient memory is available.
    /// <br/>
    /// ref: <a href="https://docs.microsoft.com/en-us/dotnet/standard/garbage-collection/latency">Microsoft Docs/Garbase-Collection/Latency</a>
    /// </summary>
    public readonly GCLatencyMode LatencyMode;
    public readonly long HeapSize;
    public readonly int Gen0Count;
    public readonly int Gen1Count;
    public readonly int Gen2Count;
    /// <summary>
    /// Percent
    /// </summary>
    public readonly int TimeInGc;
    /// <summary>
    /// bytes
    /// </summary>
    public readonly ulong Gen0Size;
    /// <summary>
    /// bytes
    /// </summary>
    public readonly ulong Gen1Size;
    /// <summary>
    /// bytes
    /// </summary>
    public readonly ulong Gen2Size;
    /// <summary>
    /// bytes
    /// </summary>
    public readonly ulong LohSize;

    public GCInfoStatistics(DateTime date, GCMode gCMode, GCLargeObjectHeapCompactionMode compactionMode, GCLatencyMode latencyMode, long heapSize, int gen0Count, int gen1Count, int gen2Count, int timeInGc, ulong gen0Size, ulong gen1Size, ulong gen2Size, ulong lohSize)
    {
        Date = date;
        GCMode = gCMode;
        CompactionMode = compactionMode;
        LatencyMode = latencyMode;
        HeapSize = heapSize;
        Gen0Count = gen0Count;
        Gen1Count = gen1Count;
        Gen2Count = gen2Count;
        TimeInGc = timeInGc;
        Gen0Size = gen0Size;
        Gen1Size = gen1Size;
        Gen2Size = gen2Size;
        LohSize = lohSize;
    }

    public override bool Equals(object? obj)
    {
        return obj is GCInfoStatistics statistics && Equals(statistics);
    }

    public bool Equals([AllowNull] GCInfoStatistics other)
    {
        return Date == other.Date &&
               GCMode == other.GCMode &&
               EqualityComparer<GCLargeObjectHeapCompactionMode>.Default.Equals(CompactionMode, other.CompactionMode) &&
               EqualityComparer<GCLatencyMode>.Default.Equals(LatencyMode, other.LatencyMode) &&
               HeapSize == other.HeapSize &&
               Gen0Count == other.Gen0Count &&
               Gen1Count == other.Gen1Count &&
               Gen2Count == other.Gen2Count &&
               TimeInGc == other.TimeInGc &&
               Gen0Size == other.Gen0Size &&
               Gen1Size == other.Gen1Size &&
               Gen2Size == other.Gen2Size &&
               LohSize == other.LohSize;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Date);
        hash.Add(GCMode);
        hash.Add(CompactionMode);
        hash.Add(LatencyMode);
        hash.Add(HeapSize);
        hash.Add(Gen0Count);
        hash.Add(Gen1Count);
        hash.Add(Gen2Count);
        hash.Add(TimeInGc);
        hash.Add(Gen0Size);
        hash.Add(Gen1Size);
        hash.Add(Gen2Size);
        hash.Add(LohSize);
        return hash.ToHashCode();
    }

    public static bool operator ==(GCInfoStatistics left, GCInfoStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GCInfoStatistics left, GCInfoStatistics right)
    {
        return !(left == right);
    }

    public string GetGCModeString()
    {
        return GCMode switch
        {
            GCMode.Workstation => "Workstation",
            GCMode.Server => "Server",
            _ => throw new ArgumentOutOfRangeException(nameof(GCMode)),
        };
    }

    public string GetCompactionModeString()
    {
        return CompactionMode switch
        {
            GCLargeObjectHeapCompactionMode.CompactOnce => "CompactOnce", // compact and reset value to default
            GCLargeObjectHeapCompactionMode.Default => "Default", // non compacting
            _ => throw new ArgumentOutOfRangeException(nameof(CompactionMode)),
        };
    }

    public string GetLatencyModeString()
    {
        return LatencyMode switch
        {
            GCLatencyMode.Batch => "Batch",
            GCLatencyMode.Interactive => "Interactive",
            GCLatencyMode.LowLatency => "LowLatency",
            GCLatencyMode.NoGCRegion => "NoGCRegion", // you can't set to this value.
            GCLatencyMode.SustainedLowLatency => "SustainedLowLatency",
            _ => throw new ArgumentOutOfRangeException(nameof(LatencyMode)),
        };
    }
}
