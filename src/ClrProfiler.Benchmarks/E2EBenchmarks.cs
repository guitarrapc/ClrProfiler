using BenchmarkDotNet.Attributes;
using System.Runtime.CompilerServices;

namespace ClrProfiler.Benchmarks;

/// <summary>
/// Measures the end-to-end cost of running representative application work with all
/// ClrProfiler listeners enabled. Compare the rows for TrackingEnabled=false and true.
/// </summary>
[MemoryDiagnoser]
public class E2EBenchmarks
{
    private const int AllocationCount = 1_024;
    private const int AllocationSize = 1_024;
    private const int ParallelOperationCount = 1_000;

    private readonly object contentionGate = new();
    private CancellationTokenSource? cancellationTokenSource;
    private ProfilerTracker? tracker;
    private Exception? callbackException;
    private int contentionCounter;

    [Params(false, true)]
    public bool TrackingEnabled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (!TrackingEnabled)
        {
            return;
        }

        cancellationTokenSource = new CancellationTokenSource();
        tracker = new ProfilerTracker(new ProfilerTrackerOptions
        {
            CancellationTokenSource = cancellationTokenSource,
            // Timer samples are intentionally outside these event-oriented workloads.
            TimerOption = (Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan),
            ContentionEventCallback = (ConsumeAsync, RecordException),
            GCEventCallback = (ConsumeAsync, RecordException),
            ThreadPoolEventCallback = (ConsumeAsync, RecordException),
            GCInfoTimerCallback = (ConsumeAsync, RecordException),
            ProcessInfoTimerCallback = (ConsumeAsync, RecordException),
            ThreadInfoTimerCallback = (ConsumeAsync, RecordException),
        });
        tracker.Start();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        tracker?.Dispose();
        cancellationTokenSource?.Dispose();

        if (callbackException is not null)
        {
            throw new InvalidOperationException("A profiler callback failed during the benchmark.", callbackException);
        }
    }

    [Benchmark(Description = "Allocate 1 MiB and force Gen0 GC")]
    public long AllocationAndGc()
    {
        long checksum = 0;
        for (var i = 0; i < AllocationCount; i++)
        {
            var buffer = new byte[AllocationSize];
            buffer[0] = (byte)i;
            checksum += buffer[0];
        }

        GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: false);
        return checksum;
    }

    [Benchmark(Description = "Contended monitor")]
    public int Contention()
    {
        Parallel.For(0, ParallelOperationCount, _ =>
        {
            lock (contentionGate)
            {
                contentionCounter++;
            }
        });

        return contentionCounter;
    }

    [Benchmark(Description = "ThreadPool dispatch")]
    public Task ThreadPoolDispatch()
    {
        return Parallel.ForAsync(0, ParallelOperationCount, static (_, _) => ValueTask.CompletedTask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Task ConsumeAsync<T>(T _) => Task.CompletedTask;

    private void RecordException(Exception exception)
    {
        Interlocked.CompareExchange(ref callbackException, exception, null);
    }
}
