using ClrProfiler.Statistics;
using FluentAssertions;

namespace ClrProfilerUnitTest;

[Collection(nameof(TestCollectionDefinition))]
public class GcTimerProfilerUnitTest
{
    [Fact]
    public void GcTest()
    {
        TestHelpers.PrewarmupGC();

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

        actual.Gen0Count.Should().Be(11);
        actual.Gen0Size.Should().Be(24);
        actual.Gen1Count.Should().Be(10);
        actual.Gen1Size.Should().Be(24);
        actual.Gen2Count.Should().Be(10);

        // 1 gc
        profiler.Start();
        _ = profiler.ReadResultAsync(cts.Token);
        GC.Collect();
        while (!complete)
        {
            Thread.Sleep(50);
        }
        profiler.Stop();
        complete = false;

        actual.Gen0Count.Should().Be(12);
        actual.Gen0Size.Should().Be(24);
        actual.Gen1Count.Should().Be(11);
        actual.Gen2Count.Should().Be(11);

        // 2 gc
        profiler.Start();
        _ = profiler.ReadResultAsync(cts.Token);
        GC.Collect();
        while (!complete)
        {
            Thread.Sleep(50);
        }
        profiler.Stop();
        complete = false;

        actual.Gen0Count.Should().Be(13);
        actual.Gen0Size.Should().Be(24);
        actual.Gen1Count.Should().Be(12);
        actual.Gen2Count.Should().Be(12);

        cts.Cancel();
    }
}
