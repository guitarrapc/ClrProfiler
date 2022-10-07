using ClrProfiler.Statistics;
using FluentAssertions;

namespace ClrProfiler.UnitTest;

[Collection(nameof(TestCollectionDefinition))]
public class GcProfilerUnitTest
{
    [Fact]
    public void GCInfoTimerProfilerTest()
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
        _ = profiler.ReadResultAsync(cts.Token);
        while (!complete)
        {
            Thread.Sleep(50);
        }
        profiler.Stop();
        complete = false;

        var after2 = GC.GetTotalAllocatedBytes(true);
        var diff2 = after2 - before2; // 5288-5856

        var total = GC.GetTotalAllocatedBytes(true);

        actual.GCMode.Should().Be(GCMode.Workstation);
        actual.CompactionMode.Should().Be(System.Runtime.GCLargeObjectHeapCompactionMode.Default);
        actual.LatencyMode.Should().Be(System.Runtime.GCLatencyMode.Interactive);

        actual.Gen0Count.Should().Be(gen0GCCount);
        actual.Gen1Count.Should().Be(gen1GCCount);
        actual.Gen2Count.Should().Be(gen2GCCount);
        //actual.Gen0Size.Should().Be(24);
        //actual.Gen1Size.Should().Be(24);

        // 1
        profiler.Start();
        _ = profiler.ReadResultAsync(cts.Token);
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

        actual.Gen0Count.Should().Be(gen0GCCount);
        actual.Gen1Count.Should().Be(gen1GCCount);
        actual.Gen2Count.Should().Be(gen2GCCount);
        //actual.Gen0Size.Should().Be(24);

        // 2
        profiler.Start();
        _ = profiler.ReadResultAsync(cts.Token);
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

        actual.Gen0Count.Should().Be(gen0GCCount);
        actual.Gen1Count.Should().Be(gen1GCCount);
        actual.Gen2Count.Should().Be(gen2GCCount);
        //actual.Gen0Size.Should().Be(24);

        cts.Cancel();
    }
}
