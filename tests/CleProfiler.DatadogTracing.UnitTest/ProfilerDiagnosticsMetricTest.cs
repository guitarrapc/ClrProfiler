using ClrProfiler;
using ClrProfiler.DatadogTracing;
using ClrProfiler.Statistics;
using ClrProfiler.TimerListeners;
using Microsoft.Extensions.Logging;

namespace CleProfiler.DatadogTracing.UnitTest;

/// <summary>
/// Covers the projection of per-profiler dropped event counts, the metric an operator reads to tell
/// whether the profiler itself lost data.
/// </summary>
public class ProfilerDiagnosticsMetricTest
{
    [Test]
    public async Task ProfilerDiagnosticsTimerGauge_ProjectsCumulativeCountWithProfilerTag()
    {
        var logger = new CapturingLogger();

        LoggerTracing.ProfilerDiagnosticsTimerGauge(
            new ProfilerDiagnosticsStatistics(DateTime.UnixEpoch, nameof(GCEventProfiler), 42),
            logger);

        var message = await Assert.That(logger.Messages).HasSingleItem();
        await Assert.That(message).Contains("clr_diagnostics_timer.profiler.dropped_event_count: 42,");
        await Assert.That(message).Contains($"tags: profiler:{nameof(GCEventProfiler)}");
    }

    [Test]
    [Arguments(nameof(GCEventProfiler))]
    [Arguments(nameof(ThreadPoolEventProfiler))]
    [Arguments(nameof(ContentionEventProfiler))]
    [Arguments(nameof(ThreadInfoTimerProfiler))]
    [Arguments(nameof(GCInfoTimerProfiler))]
    [Arguments(nameof(ProcessInfoTimerProfiler))]
    [Arguments(nameof(ProfilerDiagnosticsTimerProfiler))]
    public async Task ProfilerDiagnosticsTimerGauge_TagsEveryBuiltInProfilerByName(string profilerName)
    {
        var logger = new CapturingLogger();

        LoggerTracing.ProfilerDiagnosticsTimerGauge(
            new ProfilerDiagnosticsStatistics(DateTime.UnixEpoch, profilerName, 1),
            logger);

        var message = await Assert.That(logger.Messages).HasSingleItem();
        await Assert.That(message).Contains($"tags: profiler:{profilerName}");
    }

    [Test]
    [Arguments("SomeCustomProfiler")]
    [Arguments("")]
    public async Task ProfilerDiagnosticsTimerGauge_UsesBoundedUnknownTagForUnrecognizedProfilers(string profilerName)
    {
        var logger = new CapturingLogger();

        LoggerTracing.ProfilerDiagnosticsTimerGauge(
            new ProfilerDiagnosticsStatistics(DateTime.UnixEpoch, profilerName, 1),
            logger);

        var message = await Assert.That(logger.Messages).HasSingleItem();
        await Assert.That(message).Contains("tags: profiler:unknown");
    }

    [Test]
    public async Task ProfilerTags_ReuseTheSamePrecomputedArrayInstance()
    {
        // Tag sets are precomputed over a bounded key space, so repeated projection must not build
        // a new array per sample and an arbitrary name must not create a cache entry.
        var first = MetricTags.GetProfiler(nameof(GCEventProfiler)).Values;
        var second = MetricTags.GetProfiler(nameof(GCEventProfiler)).Values;
        var unknownFirst = MetricTags.GetProfiler("first-unknown").Values;
        var unknownSecond = MetricTags.GetProfiler("second-unknown").Values;
        var unknownText = MetricTags.GetProfiler("third-unknown").Text;

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(ReferenceEquals(unknownFirst, unknownSecond)).IsTrue();
        await Assert.That(unknownText).IsEqualTo("profiler:unknown");
    }

    [Test]
    public async Task DatadogAndLoggerHandlers_BothProjectTheDiagnosticsSample()
    {
        var logger = new CapturingLogger();
        var statistics = new ProfilerDiagnosticsStatistics(DateTime.UnixEpoch, nameof(ContentionEventProfiler), 5);

        // Both handlers must accept the sample; only the logger handler is observable here.
        await new DatadogTrackerCallbackHandler(logger).OnProfilerDiagnosticsTimerAsync(statistics);
        await new LoggerTrackerCallbackHandler(logger).OnProfilerDiagnosticsTimerAsync(statistics);

        var message = await Assert.That(logger.Messages).HasSingleItem();
        await Assert.That(message).Contains("clr_diagnostics_timer.profiler.dropped_event_count: 5,");
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
