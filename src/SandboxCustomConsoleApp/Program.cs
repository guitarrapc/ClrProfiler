using ClrProfiler;
using ClrProfiler.DatadogTracing;
using ClrProfiler.Statistics;
using Microsoft.Extensions.Logging;
using ZLogger;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.ClearProviders();
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddZLoggerConsole();
});
var logger = loggerFactory.CreateLogger<Program>();

// custom tracker
var tracker = new ClrTracker(loggerFactory, new ClrTrackerOptions
{
    TrackerType = ClrTrackerType.Custom,
    CustomHandler = new CustomClrTrackerCallbackHandler(logger),
});
tracker.EnableTracker();
tracker.StartTracker();

public class CustomClrTrackerCallbackHandler(ILogger logger) : IClrTrackerCallbackHandler
{
    public Task OnContentionEventAsync(ContentionEventStatistics statistics)
    {
        logger.LogInformation(nameof(OnContentionEventAsync));
        return Task.CompletedTask;
    }

    public void OnException(Exception exception)
    {
        logger.LogInformation(nameof(OnException));
    }

    public Task OnGCEventAsync(GCEventStatistics statistics)
    {
        logger.LogInformation(nameof(OnGCEventAsync));
        return Task.CompletedTask;
    }

    public Task OnGCInfoTimerAsync(GCInfoStatistics statistics)
    {
        logger.LogInformation(nameof(OnGCInfoTimerAsync));
        return Task.CompletedTask;
    }

    public Task OnProcessInfoTimerAsync(ProcessInfoStatistics statistics)
    {
        logger.LogInformation(nameof(OnProcessInfoTimerAsync));
        return Task.CompletedTask;
    }

    public Task OnThreadInfoTimerAsync(ThreadInfoStatistics statistics)
    {
        logger.LogInformation(nameof(OnThreadInfoTimerAsync));
        return Task.CompletedTask;
    }

    public Task OnThreadPoolEventAsync(ThreadPoolEventStatistics statistics)
    {
        logger.LogInformation(nameof(OnThreadPoolEventAsync));
        return Task.CompletedTask;
    }
}
