using ClrProfiler.Statistics;
using System.Runtime;

namespace ClrProfiler.DatadogTracing;

internal readonly struct MetricTagSet
{
    public readonly string[] Values;
    public readonly string Text;

    public MetricTagSet(string[] values)
    {
        Values = values;
        Text = string.Join(',', values);
    }
}

internal readonly struct GcInfoMetricTagSet
{
    public readonly MetricTagSet Base;
    public readonly MetricTagSet Gen0;
    public readonly MetricTagSet Gen1;
    public readonly MetricTagSet Gen2;
    public readonly MetricTagSet Loh;

    public GcInfoMetricTagSet(MetricTagSet @base)
    {
        Base = @base;
        Gen0 = WithGeneration("gc_gen:0", @base.Values);
        Gen1 = WithGeneration("gc_gen:1", @base.Values);
        Gen2 = WithGeneration("gc_gen:2", @base.Values);
        Loh = WithGeneration("gc_gen:loh", @base.Values);
    }

    private static MetricTagSet WithGeneration(string generation, string[] baseTags)
    {
        return new MetricTagSet([generation, baseTags[0], baseTags[1], baseTags[2]]);
    }
}

internal static class MetricTags
{
    private const int GcGenerationCount = 3;
    private const int GcTypeCount = 3;
    private const int GcReasonCount = 11;
    private const int GcModeCount = 2;
    private const int GcLatencyModeCount = 5;
    private const int GcCompactionModeCount = 2;

    private static readonly string[] GcReasonNames =
    [
        "soh",
        "induced",
        "low_memory",
        "empty",
        "loh",
        "oos_soh",
        "oos_loh",
        "incuded_non_forceblock",
        "stress_testing",
        "finalizer_low_memory_induced",
        "user_gc_request",
    ];

    private static readonly string[] GcSuspendReasonNames =
    [
        "other",
        "gc",
        "appdomain_shudown",
        "code_pitch",
        "shutdown",
        "debugger",
        "prep_gc",
    ];

    private static readonly string[] ThreadAdjustmentReasonNames =
    [
        "warmup",
        "initializing",
        "random_move",
        "climbing_move",
        "change_point",
        "stabilizing",
        "starvation",
        "timedout",
        "cooperative_blocking",
    ];

    private static readonly MetricTagSet[] ContentionTags = CreateSingleValueTags("contention_type:", 2);
    private static readonly MetricTagSet[] GcStartEndTags = CreateGcStartEndTags();
    private static readonly MetricTagSet[] GcSuspendTags = CreateNamedTags("gc_suspend_reason:", GcSuspendReasonNames);
    private static readonly MetricTagSet[] ThreadAdjustmentTags = CreateNamedTags("thread_adjust_reason:", ThreadAdjustmentReasonNames);
    private static readonly GcInfoMetricTagSet[] GcInfoTags = CreateGcInfoTags();

    public static ref readonly MetricTagSet GetContention(byte flag)
    {
        if (flag >= ContentionTags.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(flag), flag, "Contention flag is not defined.");
        }

        return ref ContentionTags[flag];
    }

    public static ref readonly MetricTagSet GetGcStartEnd(uint generation, uint type, uint reason)
    {
        if (generation >= GcGenerationCount)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), generation, "GC generation is not defined.");
        }
        if (type >= GcTypeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "GC type is not defined.");
        }
        if (reason >= GcReasonCount)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "GC reason is not defined.");
        }

        var index = ((int)generation * GcTypeCount * GcReasonCount) + ((int)type * GcReasonCount) + (int)reason;
        return ref GcStartEndTags[index];
    }

    public static ref readonly MetricTagSet GetGcSuspend(uint reason)
    {
        if (reason >= GcSuspendTags.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "GC suspend reason is not defined.");
        }

        return ref GcSuspendTags[reason];
    }

    public static ref readonly MetricTagSet GetThreadAdjustment(uint reason)
    {
        if (reason >= ThreadAdjustmentTags.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Thread adjustment reason is not defined.");
        }

        return ref ThreadAdjustmentTags[reason];
    }

    public static ref readonly GcInfoMetricTagSet GetGcInfo(GCMode mode, GCLatencyMode latencyMode, GCLargeObjectHeapCompactionMode compactionMode)
    {
        var modeIndex = mode switch
        {
            GCMode.Workstation => 0,
            GCMode.Server => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "GC mode is not defined."),
        };
        var latencyIndex = latencyMode switch
        {
            GCLatencyMode.Batch => 0,
            GCLatencyMode.Interactive => 1,
            GCLatencyMode.LowLatency => 2,
            GCLatencyMode.SustainedLowLatency => 3,
            GCLatencyMode.NoGCRegion => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(latencyMode), latencyMode, "GC latency mode is not defined."),
        };
        var compactionIndex = compactionMode switch
        {
            GCLargeObjectHeapCompactionMode.Default => 0,
            GCLargeObjectHeapCompactionMode.CompactOnce => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(compactionMode), compactionMode, "GC compaction mode is not defined."),
        };

        var index = (modeIndex * GcLatencyModeCount * GcCompactionModeCount) + (latencyIndex * GcCompactionModeCount) + compactionIndex;
        return ref GcInfoTags[index];
    }

    private static MetricTagSet[] CreateSingleValueTags(string prefix, int count)
    {
        var tags = new MetricTagSet[count];
        for (var i = 0; i < tags.Length; i++)
        {
            tags[i] = new MetricTagSet([$"{prefix}{i}"]);
        }
        return tags;
    }

    private static MetricTagSet[] CreateNamedTags(string prefix, string[] names)
    {
        var tags = new MetricTagSet[names.Length];
        for (var i = 0; i < tags.Length; i++)
        {
            tags[i] = new MetricTagSet([$"{prefix}{names[i]}"]);
        }
        return tags;
    }

    private static MetricTagSet[] CreateGcStartEndTags()
    {
        var tags = new MetricTagSet[GcGenerationCount * GcTypeCount * GcReasonCount];
        for (var generation = 0; generation < GcGenerationCount; generation++)
        {
            for (var type = 0; type < GcTypeCount; type++)
            {
                for (var reason = 0; reason < GcReasonCount; reason++)
                {
                    var index = (generation * GcTypeCount * GcReasonCount) + (type * GcReasonCount) + reason;
                    tags[index] = new MetricTagSet([$"gc_gen:{generation}", $"gc_type:{type}", $"gc_reason:{GcReasonNames[reason]}"]);
                }
            }
        }
        return tags;
    }

    private static GcInfoMetricTagSet[] CreateGcInfoTags()
    {
        var modeNames = new[] { "Workstation", "Server" };
        var latencyNames = new[] { "Batch", "Interactive", "LowLatency", "SustainedLowLatency", "NoGCRegion" };
        var compactionNames = new[] { "Default", "CompactOnce" };
        var tags = new GcInfoMetricTagSet[GcModeCount * GcLatencyModeCount * GcCompactionModeCount];

        for (var mode = 0; mode < GcModeCount; mode++)
        {
            for (var latency = 0; latency < GcLatencyModeCount; latency++)
            {
                for (var compaction = 0; compaction < GcCompactionModeCount; compaction++)
                {
                    var index = (mode * GcLatencyModeCount * GcCompactionModeCount) + (latency * GcCompactionModeCount) + compaction;
                    var baseTags = new MetricTagSet(
                    [
                        $"gc_mode:{modeNames[mode]}",
                        $"latency_mode:{latencyNames[latency]}",
                        $"compaction_mode:{compactionNames[compaction]}",
                    ]);
                    tags[index] = new GcInfoMetricTagSet(baseTags);
                }
            }
        }

        return tags;
    }
}
