namespace ClrProfilerUnitTest;

public static class TestHelpers
{
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
}
