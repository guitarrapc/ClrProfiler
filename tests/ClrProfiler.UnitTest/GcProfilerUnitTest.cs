using ClrProfiler.Statistics;

namespace ClrProfiler.UnitTest;

[NotInParallel]
public class GcProfilerUnitTest
{
    [Test]
    public async Task GCInfoTimerProfilerTest()
    {
        var before = GC.GetTotalAllocatedBytes(true);
        using var cts = new CancellationTokenSource();
        var complete = false;
        var actual = new GCInfoStatistics();
        Func<GCInfoStatistics, Task> onSuccess = async (statistics) =>
        {
            actual = statistics;
            complete = true;
        };
        Action<Exception> onError = (exception) =>
        {
            complete = true;
            throw new Exception("Exception Happen", exception);
        };
        var timer = (TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100));
        using var profiler = new GCInfoTimerProfiler(onSuccess, onError, timer);
        var after = GC.GetTotalAllocatedBytes(true);
        var diff = after - before;

        var gen0GCCount = GC.CollectionCount(0) + TestHelpers.WARMUP_GC_COUNT;
        var gen1GCCount = GC.CollectionCount(1) + TestHelpers.WARMUP_GC_COUNT;
        var gen2GCCount = GC.CollectionCount(2) + TestHelpers.WARMUP_GC_COUNT;
        TestHelpers.PrewarmupGC();

        // RunProfile
        var before2 = GC.GetTotalAllocatedBytes(true);
        profiler.Start();
        var readerTask = profiler.ReadResultAsync(cts.Token);
        while (!complete)
        {
            Thread.Sleep(50);
        }
        profiler.Stop();
        complete = false;

        var after2 = GC.GetTotalAllocatedBytes(true);
        var diff2 = after2 - before2; // 5288-5856

        var total = GC.GetTotalAllocatedBytes(true);

        await Assert.That(actual.GCMode).IsEqualTo(GCMode.Workstation);
        await Assert.That(actual.CompactionMode).IsEqualTo(System.Runtime.GCLargeObjectHeapCompactionMode.Default);
        await Assert.That(actual.LatencyMode).IsEqualTo(System.Runtime.GCLatencyMode.Interactive);

        await Assert.That(actual.Gen0Count).IsEqualTo(gen0GCCount);
        await Assert.That(actual.Gen1Count).IsEqualTo(gen1GCCount);
        await Assert.That(actual.Gen2Count).IsEqualTo(gen2GCCount);
        //await Assert.That(actual.Gen0Size).IsEqualTo(24);
        //await Assert.That(actual.Gen1Size).IsEqualTo(24);

        // 1
        profiler.Start();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        gen0GCCount++;
        gen1GCCount++;
        gen2GCCount++;
        while (!complete)
        {
            Thread.Sleep(50);
        }
        profiler.Stop();
        complete = false;

        await Assert.That(actual.Gen0Count).IsEqualTo(gen0GCCount);
        await Assert.That(actual.Gen1Count).IsEqualTo(gen1GCCount);
        await Assert.That(actual.Gen2Count).IsEqualTo(gen2GCCount);
        //await Assert.That(actual.Gen0Size).IsEqualTo(24);

        // 2
        profiler.Start();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        gen0GCCount++;
        gen1GCCount++;
        gen2GCCount++;
        while (!complete)
        {
            Thread.Sleep(50);
        }
        profiler.Stop();
        complete = false;

        await Assert.That(actual.Gen0Count).IsEqualTo(gen0GCCount);
        await Assert.That(actual.Gen1Count).IsEqualTo(gen1GCCount);
        await Assert.That(actual.Gen2Count).IsEqualTo(gen2GCCount);
        //await Assert.That(actual.Gen0Size).IsEqualTo(24);

        cts.Cancel();
        await readerTask;
    }
}
