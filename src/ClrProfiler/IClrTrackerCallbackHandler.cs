using ClrProfiler.Statistics;

namespace ClrProfiler;

public interface IClrTrackerCallbackHandler
{
    /// <summary>
    /// Contention Event
    /// </summary>
    /// <param name="statistics"></param>
    /// <returns></returns>
    Task OnContentionEventAsync(ContentionEventStatistics statistics);
    /// <summary>
    /// GC Event
    /// </summary>
    /// <param name="statistics"></param>
    /// <returns></returns>
    Task OnGCEventAsync(GCEventStatistics statistics);
    /// <summary>
    /// ThreadPool Event
    /// </summary>
    /// <param name="statistics"></param>
    /// <returns></returns>
    Task OnThreadPoolEventAsync(ThreadPoolEventStatistics statistics);
    /// <summary>
    /// GCInfo
    /// </summary>
    /// <param name="statistics"></param>
    /// <returns></returns>
    Task OnGCInfoTimerAsync(GCInfoStatistics statistics);
    /// <summary>
    /// ProcessInfo
    /// </summary>
    /// <param name="statistics"></param>
    /// <returns></returns>
    Task OnProcessInfoTimerAsync(ProcessInfoStatistics statistics);
    /// <summary>
    /// ThreadInfo
    /// </summary>
    /// <param name="statistics"></param>
    /// <returns></returns>
    Task OnThreadInfoTimerAsync(ThreadInfoStatistics statistics);
    /// <summary>
    /// On Exception
    /// </summary>
    /// <param name="exception"></param>
    void OnException(Exception exception);
}
