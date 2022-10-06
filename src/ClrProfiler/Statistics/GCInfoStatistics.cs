using System.Diagnostics.CodeAnalysis;
using System.Runtime;

namespace ClrProfiler.Statistics;

public enum GCMode
{
    Workstation = 0,
    Server = 1,
}
public struct GCInfoStatistics : IEquatable<GCInfoStatistics>
{
    public DateTime Date { get; set; }
    public GCMode GCMode { get; set; }
    public GCLargeObjectHeapCompactionMode CompactionMode { get; set; }
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
    public GCLatencyMode LatencyMode { get; set; }
    public long HeapSize { get; set; }
    public int Gen0Count { get; set; }
    public int Gen1Count { get; set; }
    public int Gen2Count { get; set; }
    /// <summary>
    /// Percent
    /// </summary>
    public int TimeInGc { get; set; }
    /// <summary>
    /// bytes
    /// </summary>
    public ulong Gen0Size { get; set; }
    /// <summary>
    /// bytes
    /// </summary>
    public ulong Gen1Size { get; set; }
    /// <summary>
    /// bytes
    /// </summary>
    public ulong Gen2Size { get; set; }
    /// <summary>
    /// bytes
    /// </summary>
    public ulong LohSize { get; set; }

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
