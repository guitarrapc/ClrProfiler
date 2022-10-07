using System.Diagnostics.CodeAnalysis;

namespace ClrProfiler.Statistics;

public readonly struct ThreadInfoStatistics : IEquatable<ThreadInfoStatistics>
{
    public readonly DateTime Date;
    public readonly int AvailableWorkerThreads;
    public readonly int AvailableCompletionPortThreads;
    public readonly int MaxWorkerThreads;
    public readonly int MaxCompletionPortThreads;
    public readonly int ThreadCount;
    public readonly long QueueLength;
    public readonly long CompletedItemsCount;
    public readonly long LockContentionCount;

    public ThreadInfoStatistics(DateTime date, int availableWorkerThreads, int availableCompletionPortThreads, int maxWorkerThreads, int maxCompletionPortThreads, int threadCount, long queueLength, long completedItemsCount, long lockContentionCount)
    {
        Date = date;
        AvailableWorkerThreads = availableWorkerThreads;
        AvailableCompletionPortThreads = availableCompletionPortThreads;
        MaxWorkerThreads = maxWorkerThreads;
        MaxCompletionPortThreads = maxCompletionPortThreads;
        ThreadCount = threadCount;
        QueueLength = queueLength;
        CompletedItemsCount = completedItemsCount;
        LockContentionCount = lockContentionCount;
    }

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
