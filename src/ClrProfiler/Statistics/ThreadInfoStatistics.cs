using System.Diagnostics.CodeAnalysis;

namespace ClrProfiler.Statistics;

public struct ThreadInfoStatistics : IEquatable<ThreadInfoStatistics>
{
    public DateTime Date { get; set; }
    public int AvailableWorkerThreads { get; set; }
    public int AvailableCompletionPortThreads { get; set; }
    public int MaxWorkerThreads { get; set; }
    public int MaxCompletionPortThreads { get; set; }
    public int ThreadCount { get; set; }
    public long QueueLength { get; set; }
    public long CompletedItemsCount { get; set; }
    public long LockContentionCount { get; set; }

    public override bool Equals(object? obj)
    {
        return obj is ThreadInfoStatistics statistics && Equals(statistics);
    }

    public bool Equals([AllowNull] ThreadInfoStatistics other)
    {
        return Date == other.Date &&
               AvailableWorkerThreads == other.AvailableWorkerThreads &&
               AvailableCompletionPortThreads == other.AvailableCompletionPortThreads &&
               MaxWorkerThreads == other.MaxWorkerThreads &&
               MaxCompletionPortThreads == other.MaxCompletionPortThreads &&
               ThreadCount == other.ThreadCount &&
               QueueLength == other.QueueLength &&
               CompletedItemsCount == other.CompletedItemsCount &&
               LockContentionCount == other.LockContentionCount;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Date);
        hash.Add(AvailableWorkerThreads);
        hash.Add(AvailableCompletionPortThreads);
        hash.Add(MaxWorkerThreads);
        hash.Add(MaxCompletionPortThreads);
        hash.Add(ThreadCount);
        hash.Add(QueueLength);
        hash.Add(CompletedItemsCount);
        hash.Add(LockContentionCount);
        return hash.ToHashCode();
    }

    public static bool operator ==(ThreadInfoStatistics left, ThreadInfoStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ThreadInfoStatistics left, ThreadInfoStatistics right)
    {
        return !(left == right);
    }
}
