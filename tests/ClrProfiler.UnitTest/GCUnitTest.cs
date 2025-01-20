namespace ClrProfiler.UnitTest;

//[Collection(nameof(TestCollectionDefinition))]
//public class GCUnitTest
//{
//    [Fact, TestPriority(999)]
//    public async Task GcAllocateArray100Test()
//    {
//        TestHelpers.PrewarmupGC();

//        var before = GC.GetTotalMemory(true);
//        // int array and allocation size list.
//        // LENGTH | ALLOCATION
//        // ------ | ---------
//        //      0 | 312    (0)
//        //      1 | 32     (8)
//        //     10 | 352    (40)
//        //    100 | 424    (400)
//        //   1000 | 4024   (4000)
//        //  10000 | 40312  (40000)
//        // 100000 | 400312 (400024)
//        var x = new int[100];
//        var after = GC.GetTotalMemory(true);
//        var actual = after - before;

//        actual.Should().Be(400);
//    }
//}
