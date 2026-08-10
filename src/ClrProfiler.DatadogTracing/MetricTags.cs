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

    public GcInfoMetricTagSet(MetricTagSet @base, string gen0, string gen1, string gen2, string loh)
    {
        Base = @base;
        Gen0 = WithGeneration(gen0, @base.Values);
        Gen1 = WithGeneration(gen1, @base.Values);
        Gen2 = WithGeneration(gen2, @base.Values);
        Loh = WithGeneration(loh, @base.Values);
    }

    private static MetricTagSet WithGeneration(string generation, string[] baseTags)
    {
        return new MetricTagSet([generation, baseTags[0], baseTags[1], baseTags[2]]);
    }
}

internal static class MetricTags
{
    private const int KnownContentionFlagCount = 2;
    private const int KnownGcGenerationCount = 3;
    private const int KnownGcTypeCount = 3;
    private const int KnownGcReasonCount = 11;
    private const int KnownGcModeCount = 2;
    private const int KnownGcLatencyModeCount = 5;
    private const int KnownGcCompactionModeCount = 2;
    private const int KnownGcSuspendReasonCount = 7;
    private const int KnownThreadAdjustmentReasonCount = 9;
    private const int GcGenerationCount = KnownGcGenerationCount + 1;
    private const int GcTypeCount = KnownGcTypeCount + 1;
    private const int GcReasonCount = KnownGcReasonCount + 1;
    private const int GcModeCount = KnownGcModeCount + 1;
    private const int GcLatencyModeCount = KnownGcLatencyModeCount + 1;
    private const int GcCompactionModeCount = KnownGcCompactionModeCount + 1;

    private static readonly string[] ContentionTagValues = CreateNumericTagValues("contention_type:", KnownContentionFlagCount);
    private static readonly string[] GcGenerationTagValues = CreateNumericTagValues("gc_gen:", KnownGcGenerationCount);
    private static readonly string[] GcTypeTagValues = CreateNumericTagValues("gc_type:", KnownGcTypeCount);
    private static readonly string[] GcReasonTagValues = CreateGcReasonTagValues();
    private static readonly string[] GcSuspendTagValues = CreateGcSuspendTagValues();
    private static readonly string[] ThreadAdjustmentTagValues = CreateThreadAdjustmentTagValues();
    private static readonly string[] GcModeTagValues = CreateGcModeTagValues();
    private static readonly string[] GcLatencyModeTagValues = CreateGcLatencyModeTagValues();
    private static readonly string[] GcCompactionModeTagValues = CreateGcCompactionModeTagValues();

    private static readonly MetricTagSet[] ContentionTags = CreateSingleValueTagSets(ContentionTagValues);
    private static readonly MetricTagSet[] GcStartEndTags = CreateGcStartEndTags();
    private static readonly MetricTagSet[] GcSuspendTags = CreateSingleValueTagSets(GcSuspendTagValues);
    private static readonly MetricTagSet[] ThreadAdjustmentTags = CreateSingleValueTagSets(ThreadAdjustmentTagValues);
    private static readonly GcInfoMetricTagSet[] GcInfoTags = CreateGcInfoTags();

    static MetricTags()
    {
    }

    public static void Initialize()
    {
        // The explicit static constructor guarantees initialization before this method runs.
        GC.KeepAlive(GcInfoTags);
    }

    public static ref readonly MetricTagSet GetContention(byte flag)
    {
        var index = flag < KnownContentionFlagCount ? flag : KnownContentionFlagCount;
        return ref ContentionTags[index];
    }

    public static ref readonly MetricTagSet GetGcStartEnd(uint generation, uint type, uint reason)
    {
        var generationIndex = generation < KnownGcGenerationCount ? (int)generation : KnownGcGenerationCount;
        var typeIndex = type < KnownGcTypeCount ? (int)type : KnownGcTypeCount;
        var reasonIndex = reason < KnownGcReasonCount ? (int)reason : KnownGcReasonCount;
        var index = (generationIndex * GcTypeCount * GcReasonCount) + (typeIndex * GcReasonCount) + reasonIndex;
        return ref GcStartEndTags[index];
    }

    public static ref readonly MetricTagSet GetGcSuspend(uint reason)
    {
        var index = reason < KnownGcSuspendReasonCount ? (int)reason : KnownGcSuspendReasonCount;
        return ref GcSuspendTags[index];
    }

    public static ref readonly MetricTagSet GetThreadAdjustment(uint reason)
    {
        var index = reason < KnownThreadAdjustmentReasonCount ? (int)reason : KnownThreadAdjustmentReasonCount;
        return ref ThreadAdjustmentTags[index];
    }

    public static ref readonly GcInfoMetricTagSet GetGcInfo(GCMode mode, GCLatencyMode latencyMode, GCLargeObjectHeapCompactionMode compactionMode)
    {
        var modeIndex = mode switch
        {
            GCMode.Workstation => 0,
            GCMode.Server => 1,
            _ => KnownGcModeCount,
        };
        var latencyIndex = latencyMode switch
        {
            GCLatencyMode.Batch => 0,
            GCLatencyMode.Interactive => 1,
            GCLatencyMode.LowLatency => 2,
            GCLatencyMode.SustainedLowLatency => 3,
            GCLatencyMode.NoGCRegion => 4,
            _ => KnownGcLatencyModeCount,
        };
        var compactionIndex = compactionMode switch
        {
            GCLargeObjectHeapCompactionMode.Default => 0,
            GCLargeObjectHeapCompactionMode.CompactOnce => 1,
            _ => KnownGcCompactionModeCount,
        };

        var index = (modeIndex * GcLatencyModeCount * GcCompactionModeCount) + (latencyIndex * GcCompactionModeCount) + compactionIndex;
        return ref GcInfoTags[index];
    }

    private static string[] CreateNumericTagValues(string prefix, int knownCount)
    {
        var values = new string[knownCount + 1];
        for (var i = 0; i < knownCount; i++)
        {
            values[i] = $"{prefix}{i}";
        }
        values[knownCount] = $"{prefix}unknown";
        return values;
    }

    private static string[] CreateGcReasonTagValues()
    {
        var values = new string[KnownGcReasonCount + 1];
        for (var reason = 0; reason < KnownGcReasonCount; reason++)
        {
            var statistics = new GCStartEndStatistics(0, 0, 0, (uint)reason, 0, 0, 0);
            values[reason] = $"gc_reason:{statistics.GetReasonString()}";
        }
        values[KnownGcReasonCount] = "gc_reason:unknown";
        return values;
    }

    private static string[] CreateGcSuspendTagValues()
    {
        var values = new string[KnownGcSuspendReasonCount + 1];
        for (var reason = 0; reason < KnownGcSuspendReasonCount; reason++)
        {
            var statistics = new GCSuspendStatistics(0, (uint)reason, 0);
            values[reason] = $"gc_suspend_reason:{statistics.GetReasonString()}";
        }
        values[KnownGcSuspendReasonCount] = "gc_suspend_reason:unknown";
        return values;
    }

    private static string[] CreateThreadAdjustmentTagValues()
    {
        var values = new string[KnownThreadAdjustmentReasonCount + 1];
        for (var reason = 0; reason < KnownThreadAdjustmentReasonCount; reason++)
        {
            var statistics = new ThreadPoolAdjustmentStatistics(0, 0, 0, (uint)reason);
            values[reason] = $"thread_adjust_reason:{statistics.GetReasonString()}";
        }
        values[KnownThreadAdjustmentReasonCount] = "thread_adjust_reason:unknown";
        return values;
    }

    private static string[] CreateGcModeTagValues()
    {
        return
        [
            $"gc_mode:{CreateGcInfo(GCMode.Workstation, GCLatencyMode.Interactive, GCLargeObjectHeapCompactionMode.Default).GetGCModeString()}",
            $"gc_mode:{CreateGcInfo(GCMode.Server, GCLatencyMode.Interactive, GCLargeObjectHeapCompactionMode.Default).GetGCModeString()}",
            "gc_mode:unknown",
        ];
    }

    private static string[] CreateGcLatencyModeTagValues()
    {
        return
        [
            $"latency_mode:{CreateGcInfo(GCMode.Workstation, GCLatencyMode.Batch, GCLargeObjectHeapCompactionMode.Default).GetLatencyModeString()}",
            $"latency_mode:{CreateGcInfo(GCMode.Workstation, GCLatencyMode.Interactive, GCLargeObjectHeapCompactionMode.Default).GetLatencyModeString()}",
            $"latency_mode:{CreateGcInfo(GCMode.Workstation, GCLatencyMode.LowLatency, GCLargeObjectHeapCompactionMode.Default).GetLatencyModeString()}",
            $"latency_mode:{CreateGcInfo(GCMode.Workstation, GCLatencyMode.SustainedLowLatency, GCLargeObjectHeapCompactionMode.Default).GetLatencyModeString()}",
            $"latency_mode:{CreateGcInfo(GCMode.Workstation, GCLatencyMode.NoGCRegion, GCLargeObjectHeapCompactionMode.Default).GetLatencyModeString()}",
            "latency_mode:unknown",
        ];
    }

    private static string[] CreateGcCompactionModeTagValues()
    {
        return
        [
            $"compaction_mode:{CreateGcInfo(GCMode.Workstation, GCLatencyMode.Interactive, GCLargeObjectHeapCompactionMode.Default).GetCompactionModeString()}",
            $"compaction_mode:{CreateGcInfo(GCMode.Workstation, GCLatencyMode.Interactive, GCLargeObjectHeapCompactionMode.CompactOnce).GetCompactionModeString()}",
            "compaction_mode:unknown",
        ];
    }

    private static GCInfoStatistics CreateGcInfo(GCMode mode, GCLatencyMode latencyMode, GCLargeObjectHeapCompactionMode compactionMode)
    {
        return new GCInfoStatistics(default, mode, compactionMode, latencyMode, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private static MetricTagSet[] CreateSingleValueTagSets(string[] values)
    {
        var tags = new MetricTagSet[values.Length];
        for (var i = 0; i < tags.Length; i++)
        {
            tags[i] = new MetricTagSet([values[i]]);
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
                    tags[index] = new MetricTagSet([GcGenerationTagValues[generation], GcTypeTagValues[type], GcReasonTagValues[reason]]);
                }
            }
        }
        return tags;
    }

    private static GcInfoMetricTagSet[] CreateGcInfoTags()
    {
        var tags = new GcInfoMetricTagSet[GcModeCount * GcLatencyModeCount * GcCompactionModeCount];
        for (var mode = 0; mode < GcModeCount; mode++)
        {
            for (var latency = 0; latency < GcLatencyModeCount; latency++)
            {
                for (var compaction = 0; compaction < GcCompactionModeCount; compaction++)
                {
                    var index = (mode * GcLatencyModeCount * GcCompactionModeCount) + (latency * GcCompactionModeCount) + compaction;
                    var baseTags = new MetricTagSet([GcModeTagValues[mode], GcLatencyModeTagValues[latency], GcCompactionModeTagValues[compaction]]);
                    tags[index] = new GcInfoMetricTagSet(
                        baseTags,
                        GcGenerationTagValues[0],
                        GcGenerationTagValues[1],
                        GcGenerationTagValues[2],
                        "gc_gen:loh");
                }
            }
        }
        return tags;
    }
}
