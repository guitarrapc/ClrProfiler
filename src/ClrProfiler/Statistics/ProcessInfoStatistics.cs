using System.Diagnostics.CodeAnalysis;

namespace ClrProfiler.Statistics;

public struct ProcessInfoStatistics : IEquatable<ProcessInfoStatistics>
{
    public DateTime Date { get; set; }
    public double Cpu { get; set; }
    /// <summary>
    /// The working set includes both shared and private data. The shared data includes the pages that contain all the 
    /// instructions that the process executes, including instructions in the process modules and the system libraries.
    /// </summary>
    public long WorkingSet { get; set; }
    /// <summary>
    /// The value returned by this property represents the current size of memory used by the process, in bytes, 
    /// that cannot be shared with other processes.
    /// </summary>
    public long PrivateBytes { get; set; }

    public override bool Equals(object? obj)
    {
        return obj is ProcessInfoStatistics statistics && Equals(statistics);
    }

    public bool Equals([AllowNull] ProcessInfoStatistics other)
    {
        return Date == other.Date &&
               Cpu == other.Cpu &&
               WorkingSet == other.WorkingSet &&
               PrivateBytes == other.PrivateBytes;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Date, Cpu, WorkingSet, PrivateBytes);
    }

    public static bool operator ==(ProcessInfoStatistics left, ProcessInfoStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ProcessInfoStatistics left, ProcessInfoStatistics right)
    {
        return !(left == right);
    }
}
