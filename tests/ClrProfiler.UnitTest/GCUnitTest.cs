namespace ClrProfiler.UnitTest;

//[NotInParallel]
//public class GCUnitTest
//{
//    [Test]
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

//        await Assert.That(actual).IsEqualTo(400);
//    }
//}
