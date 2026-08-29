using ClrProfiler.DatadogTracing;

namespace CleProfiler.DatadogTracing.UnitTest;

public class MetricNamesTest
{
    [Test]
    public async Task EventCatalog_ContainsEveryEmittedMetricOnce()
    {
        var actual = MetricNames.Event.All.ToArray();
        string[] expected =
        [
            "clr_diagnostics_event.contention.startend_count",
            "clr_diagnostics_event.contention.startend_duration_ns_sum",
            "clr_diagnostics_event.contention.startend_duration_ns_max",
            "clr_diagnostics_event.gc.startend_count",
            "clr_diagnostics_event.gc.startend_duration_ms",
            "clr_diagnostics_event.gc.suspend_object_count",
            "clr_diagnostics_event.gc.suspend_duration_ms",
            "clr_diagnostics_event.gc.heapstats_size_bytes",
            "clr_diagnostics_event.gc.heapstats_finalization_promoted_bytes",
            "clr_diagnostics_event.gc.heapstats_pinned_object_count",
            "clr_diagnostics_event.gc.heapstats_gc_handle_count",
            "clr_diagnostics_event.threadpool.available_workerthread_count",
            "clr_diagnostics_event.threadpool.adjustment_avg_throughput",
            "clr_diagnostics_event.threadpool.adjustment_new_workerthreads_count",
        ];

        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            await Assert.That(actual[i]).IsEqualTo(expected[i]);
        }
    }

    [Test]
    public async Task TimerCatalog_ContainsEveryEmittedMetricOnce()
    {
        var actual = MetricNames.Timer.All.ToArray();
        string[] expected =
        [
            "clr_diagnostics_timer.gc.heap_size_bytes",
            "clr_diagnostics_timer.gc.total_allocation_bytes",
            "clr_diagnostics_timer.gc.gc_count",
            "clr_diagnostics_timer.gc.gc_size",
            "clr_diagnostics_timer.gc.time_in_gc_percent",
            "clr_diagnostics_timer.gc.total_pause_time_ms",
            "clr_diagnostics_timer.process.cpu",
            "clr_diagnostics_timer.process.private_bytes",
            "clr_diagnostics_timer.process.working_sets",
            "clr_diagnostics_timer.thread.available_worker_threads",
            "clr_diagnostics_timer.thread.available_completion_port_threads",
            "clr_diagnostics_timer.thread.max_worker_threads",
            "clr_diagnostics_timer.thread.max_completion_port_threads",
            "clr_diagnostics_timer.thread.using_worker_threads",
            "clr_diagnostics_timer.thread.using_completion_port_threads",
            "clr_diagnostics_timer.thread.thread_count",
            "clr_diagnostics_timer.thread.queue_length",
            "clr_diagnostics_timer.thread.lock_contention_count",
            "clr_diagnostics_timer.thread.completed_items_count",
            "clr_diagnostics_timer.profiler.dropped_event_count",
        ];

        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            await Assert.That(actual[i]).IsEqualTo(expected[i]);
        }
    }
}
