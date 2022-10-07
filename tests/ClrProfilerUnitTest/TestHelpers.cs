using System.Runtime;
using Xunit.Sdk;

namespace ClrProfilerUnitTest;

public static class TestHelpers
{
    /// <summary>
    /// Pre warmup GC
    /// </summary>
    public static void PrewarmupGC()
    {
        var original = GCSettings.LargeObjectHeapCompactionMode;

        // see: https://learn.microsoft.com/en-us/dotnet/api/system.gc.collect?view=net-6.0
        // Setting the GCSettings.LargeObjectHeapCompactionMode property to GCLargeObjectHeapCompactionMode.CompactOnce ensures that both the LOH and SOH are compacted.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

        // Run GC several times and get stable for memory status
        for (var i = 0; i < 10; i++)
        {
            // Specifying true for the compacting argument guarantees a compacting, full blocking garbage collection.
            GC.Collect(2, GCCollectionMode.Forced, true, true);
        }

        GCSettings.LargeObjectHeapCompactionMode = original;
    }

    public static void Allocate10()
    {
        for (int i = 0; i < 10; i++)
        {
            int[] x = new int[100];
        }
    }

    public static void Allocate5K()
    {
        for (int i = 0; i < 5000; i++)
        {
            int[] x = new int[100];
        }
    }

    public static void AllocateGC100()
    {
        GC.AllocateArray<int>(100);
    }

    public static void AddMemoryPressureGC100KB()
    {
        GC.AddMemoryPressure(1024000); // 1MB
    }
}
