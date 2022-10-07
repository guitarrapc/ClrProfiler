using ClrProfiler.Statistics;
using FluentAssertions;

namespace ClrProfilerUnitTest;

[Collection(nameof(TestCollectionDefinition))]
public class GCUnitTest
{
    [Fact]
    public async Task GcAllocate424ByteTest()
    {
        TestHelpers.PrewarmupGC();

        var before = GC.GetTotalAllocatedBytes(true);

        // int array and allocation size list.
        // LENGTH | ALLOCATION
        // ------ | ---------
        //      0 | 312
        //      1 | 32
        //     10 | 352
        //    100 | 424
        //   1000 | 4024
        //  10000 | 40312
        // 100000 | 400312
        var x = new int[100];

        var after = GC.GetTotalAllocatedBytes(true);
        var actual = after - before;
        actual.Should().Be(424);
    }
}
