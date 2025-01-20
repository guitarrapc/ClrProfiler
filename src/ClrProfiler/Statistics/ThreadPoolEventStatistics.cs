using System.Diagnostics.CodeAnalysis;

namespace ClrProfiler.Statistics;

public enum ThreadPoolStatisticType
{
    ThreadPoolWorkerStartStop,
    ThreadPoolAdjustment,
}

/// <summary>
/// Data structure represent WorkerThreadPool statistics
/// </summary>
public readonly struct ThreadPoolEventStatistics(ThreadPoolStatisticType type, ThreadPoolWorkerStatistics threadPoolWorker, ThreadPoolAdjustmentStatistics threadPoolAdjustment) : IEquatable<ThreadPoolEventStatistics>
{
    public readonly ThreadPoolStatisticType Type = type;
    public readonly ThreadPoolWorkerStatistics ThreadPoolWorker = threadPoolWorker;
    public readonly ThreadPoolAdjustmentStatistics ThreadPoolAdjustment = threadPoolAdjustment;

    public override bool Equals(object? obj)
    {
        return obj is ThreadPoolEventStatistics statistics && Equals(statistics);
    }

    public bool Equals([AllowNull] ThreadPoolEventStatistics other)
    {
        return Type == other.Type &&
               ThreadPoolWorker.Equals(other.ThreadPoolWorker) &&
               ThreadPoolAdjustment.Equals(other.ThreadPoolAdjustment);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, ThreadPoolWorker, ThreadPoolAdjustment);
    }

    public static bool operator ==(ThreadPoolEventStatistics left, ThreadPoolEventStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ThreadPoolEventStatistics left, ThreadPoolEventStatistics right)
    {
        return !(left == right);
    }
}

/// <summary>
/// Number of worker threads that are not available to process work, but that are being held in reserve in case more threads are needed later.
/// Always 0 on ThreadPoolWorkerThreadStart and ThreadPoolWorkerThreadStop
/// </summary>
public readonly struct ThreadPoolWorkerStatistics(long time, uint activeWrokerThreads) : IEquatable<ThreadPoolWorkerStatistics>
{
    public readonly long Time = time;
    /// <summary>
    /// Number of worker threads available to process work, including those that are already processing work.
    /// </summary>
    public readonly uint ActiveWrokerThreads = activeWrokerThreads;

    public override bool Equals(object? obj)
    {
        return obj is ThreadPoolWorkerStatistics statistics && Equals(statistics);
    }

    public readonly bool Equals([AllowNull] ThreadPoolWorkerStatistics other)
    {
        return Time == other.Time &&
               ActiveWrokerThreads == other.ActiveWrokerThreads;
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(Time, ActiveWrokerThreads);
    }

    public static bool operator ==(ThreadPoolWorkerStatistics left, ThreadPoolWorkerStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ThreadPoolWorkerStatistics left, ThreadPoolWorkerStatistics right)
    {
        return !(left == right);
    }
}

public readonly struct ThreadPoolAdjustmentStatistics(long time, double averageThrouput, uint newWorkerThreads, uint reason) : IEquatable<ThreadPoolAdjustmentStatistics>
{
    public readonly long Time = time;
    public readonly double AverageThrouput = averageThrouput;
    public readonly uint NewWorkerThreads = newWorkerThreads;
    /// <summary>
    /// 0x00 - Warmup.
    /// 0x01 - Initializing.
    /// 0x02 - Random move.
    /// 0x03 - Climbing move.
    /// 0x04 - Change point.
    /// 0x05 - Stabilizing.
    /// 0x06 - Starvation.
    /// 0x07 - Thread timed out.
    /// </summary>
    /// <remarks>
    /// 0x03 isnt usable data, as it were just thread adjustment with hill climing heulistics.
    /// only tracking 0x06 stavation is enough.
    /// </remarks>
    public readonly uint Reason = reason;

    public string GetReasonString()
    {
        // https://learn.microsoft.com/en-us/dotnet/framework/performance/thread-pool-etw-events#threadpoolworkerthreadadjustmentadjustment
        // 8 is undocumented. see: https://github.com/dotnet/runtime/issues/77622
        return Reason switch
        {
            0 => "warmup",
            1 => "initializing",
            2 => "random_move",
            3 => "climbing_move",
            4 => "change_point",
            5 => "stabilizing",
            6 => "starvation",
            7 => "timedout",
            8 => "cooperative_blocking",
            _ => throw new ArgumentOutOfRangeException($"reason not defined. reason: {Reason}"),
        };
    }

    public override bool Equals(object? obj)
    {
        return obj is ThreadPoolAdjustmentStatistics statistics && Equals(statistics);
    }

    public bool Equals([AllowNull] ThreadPoolAdjustmentStatistics other)
    {
        return Time == other.Time &&
               AverageThrouput == other.AverageThrouput &&
               NewWorkerThreads == other.NewWorkerThreads &&
               Reason == other.Reason;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Time, AverageThrouput, NewWorkerThreads, Reason);
    }

    public static bool operator ==(ThreadPoolAdjustmentStatistics left, ThreadPoolAdjustmentStatistics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ThreadPoolAdjustmentStatistics left, ThreadPoolAdjustmentStatistics right)
    {
        return !(left == right);
    }
}
