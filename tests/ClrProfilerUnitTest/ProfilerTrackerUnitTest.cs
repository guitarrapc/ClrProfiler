using ClrProfiler.Statistics;
using FluentAssertions;

namespace ClrProfilerUnitTest;

[Collection(nameof(GCTestCollectionDefinition))]
public class GcTimerProfilerUnitTest
{
    [Fact]
    public async Task GcTest()
    {
        using var cts = new CancellationTokenSource();
        var complete = false;
        GCInfoStatistics actual = new GCInfoStatistics();
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

        // no gc
        profiler.Start();
        _ = profiler.ReadResultAsync(cts.Token);
        while (!complete)
        {
            await Task.Delay(50);
        }
        profiler.Stop();
        complete = false;

        var gen1_0 = actual.Gen1Size;
        var gen2_0 = actual.Gen2Size;
        var heap_0 = actual.HeapSize;
        var loh_0 = actual.LohSize;

        actual.GCMode.Should().Be(GCMode.Workstation);
        actual.CompactionMode.Should().Be(System.Runtime.GCLargeObjectHeapCompactionMode.Default);
        actual.LatencyMode.Should().Be(System.Runtime.GCLatencyMode.Interactive);

        actual.Gen0Count.Should().Be(1);
        actual.Gen0Size.Should().Be(24);
        actual.Gen1Count.Should().Be(0);
        actual.Gen2Count.Should().Be(0);
        // size is not stable. Let's omit them.

        // 1 gc
        profiler.Start();
        _ = profiler.ReadResultAsync(cts.Token);
        GC.Collect();
        while (!complete)
        {
            await Task.Delay(50);
        }
        profiler.Stop();
        complete = false;

        var gen1_1 = actual.Gen1Size;
        var gen2_1 = actual.Gen2Size;
        var heap_1 = actual.HeapSize;
        var loh_1 = actual.LohSize;

        actual.Gen0Count.Should().Be(2);
        actual.Gen0Size.Should().Be(24);
        actual.Gen1Count.Should().Be(1);
        actual.Gen2Count.Should().Be(1);

        actual.Gen1Size.Should().BeLessThan(gen1_0); // gc move to gen2. gen1 is smaller then previous
        actual.Gen2Size.Should().BeGreaterThan(gen2_0);
        //actual.HeapSize.Should().BeLessThan(heap_0); // not stable
        actual.LohSize.Should().BeGreaterThan(loh_0); // not stable

        // 2 gc
        profiler.Start();
        _ = profiler.ReadResultAsync(cts.Token);
        GC.Collect();
        while (!complete)
        {
            await Task.Delay(50);
        }
        profiler.Stop();
        complete = false;

        var gen1_2 = actual.Gen1Size;
        var gen2_2 = actual.Gen2Size;
        var heap_2 = actual.HeapSize;
        var loh_2 = actual.LohSize;

        actual.Gen0Count.Should().Be(3);
        actual.Gen0Size.Should().Be(24);
        actual.Gen1Count.Should().Be(2);
        actual.Gen2Count.Should().Be(2);

        actual.Gen1Size.Should().BeLessThan(gen1_1); // gc move to gen2. gen1 is smaller then previous
        actual.Gen2Size.Should().BeGreaterThan(gen2_1);
        //actual.HeapSize.Should().BeGreaterThan(heap_1);
        //actual.LohSize.Should().Be(loh_1); // not stable

        cts.Cancel();
    }

    [Fact]
    public async Task AllocationAndGcTest()
    {
        using var cts = new CancellationTokenSource();
        var complete = false;
        GCInfoStatistics actual = new GCInfoStatistics();
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

        // no gc
        profiler.Start();
        _ = profiler.ReadResultAsync(cts.Token);
        while (!complete)
        {
            await Task.Delay(50);
        }
        profiler.Stop();
        complete = false;

        var gen1_0 = actual.Gen1Size;
        var gen2_0 = actual.Gen2Size;
        var heap_0 = actual.HeapSize;
        var loh_0 = actual.LohSize;

        actual.GCMode.Should().Be(GCMode.Workstation);
        actual.CompactionMode.Should().Be(System.Runtime.GCLargeObjectHeapCompactionMode.Default);
        actual.LatencyMode.Should().Be(System.Runtime.GCLatencyMode.Interactive);

        actual.Gen0Count.Should().Be(1);
        actual.Gen0Size.Should().Be(24);
        actual.Gen1Count.Should().Be(0);
        actual.Gen2Count.Should().Be(0);
        // size is not stable. Let's omit them.

        // 1 gc
        profiler.Start();
        _ = profiler.ReadResultAsync(cts.Token);
        GC.Collect();
        while (!complete)
        {
            await Task.Delay(50);
        }
        profiler.Stop();
        complete = false;

        var gen1_1 = actual.Gen1Size;
        var gen2_1 = actual.Gen2Size;
        var heap_1 = actual.HeapSize;
        var loh_1 = actual.LohSize;

        actual.Gen0Count.Should().Be(2);
        actual.Gen0Size.Should().Be(24);
        actual.Gen1Count.Should().Be(1);
        actual.Gen2Count.Should().Be(1);

        actual.Gen1Size.Should().BeLessThan(gen1_0); // gc move to gen2. gen1 is smaller then previous
        actual.Gen2Size.Should().BeGreaterThan(gen2_0);
        //actual.HeapSize.Should().BeLessThan(heap_0);
        actual.LohSize.Should().Be(loh_0 + 98384);

        // 2 gc
        profiler.Start();
        _ = profiler.ReadResultAsync(cts.Token);
        GC.Collect();
        while (!complete)
        {
            await Task.Delay(50);
        }
        profiler.Stop();
        complete = false;

        var gen1_2 = actual.Gen1Size;
        var gen2_2 = actual.Gen2Size;
        var heap_2 = actual.HeapSize;
        var loh_2 = actual.LohSize;

        actual.Gen0Count.Should().Be(3);
        actual.Gen0Size.Should().Be(24);
        actual.Gen1Count.Should().Be(2);
        actual.Gen2Count.Should().Be(2);

        actual.Gen1Size.Should().BeLessThan(gen1_1); // gc move to gen2. gen1 is smaller then previous
        actual.Gen2Size.Should().BeGreaterThan(gen2_1);
        //actual.HeapSize.Should().BeGreaterThan(heap_1);
        actual.LohSize.Should().Be(loh_1);

        cts.Cancel();
    }
}
