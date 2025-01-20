using System.Diagnostics.CodeAnalysis;

namespace ClrProfiler.Statistics;

public readonly struct ThreadInfoStatistics(DateTime date, int availableWorkerThreads, int availableCompletionPortThreads, int maxWorkerThreads, int maxCompletionPortThreads, int threadCount, long queueLength, long completedItemsCount, long lockContentionCount) : IEquatable<ThreadInfoStatistics>
{
    public readonly DateTime Date = date;
    public readonly int AvailableWorkerThreads = availableWorkerThreads;
    public readonly int AvailableCompletionPortThreads = availableCompletionPortThreads;
    public readonly int MaxWorkerThreads = maxWorkerThreads;
    public readonly int MaxCompletionPortThreads = maxCompletionPortThreads;
    public readonly int UsingWorkerThreads = maxWorkerThreads - availableWorkerThreads;
    public readonly int UsingCompletionPortThreads = maxCompletionPortThreads - availableCompletionPortThreads;
    public readonly int ThreadCount = threadCount;
    public readonly long QueueLength = queueLength;
    public readonly long CompletedItemsCount = completedItemsCount;
    public readonly long LockContentionCount = lockContentionCount;

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
