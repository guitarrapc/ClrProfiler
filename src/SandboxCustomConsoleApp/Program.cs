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
using var cts = new CancellationTokenSource();

// custom tracker
var tracker = new ClrTracker(loggerFactory, new ClrTrackerOptions
{
    TrackerType = ClrTrackerType.Custom,
    CustomHandler = new CustomClrTrackerCallbackHandler(logger),
});
tracker.EnableTracker();
tracker.StartTracker();

// Allocate and GC
logger.LogInformation("Press Ctrl+C to cancel execution.");
Console.CancelKeyPress += ConsoleHelper.OnConsoleCancelKeyPress;
while (!ConsoleHelper.IsCancelPressed)
{
    Allocate10();
    Allocate5K();
    GC.Collect();
    await CreateWorkerThread100Async();
    await Task.Delay(100);
}

// stop
tracker.StopTracker();
tracker.CancelTracker();
cts.Cancel();

static async Task CreateWorkerThread100Async()
{
    var list = new List<Task>();
    for (var i = 0; i < 100; i++)
    {
        list.Add(Task.Delay(TimeSpan.FromMilliseconds(100)));
    }

    await Task.WhenAll(list);
}

static void Allocate10()
{
    for (int i = 0; i < 10; i++)
    {
        int[] x = new int[100];
    }
}

static void Allocate5K()
{
    for (int i = 0; i < 5000; i++)
    {
        int[] x = new int[100];
    }
}

public static class ConsoleHelper
{
    public static bool IsCancelPressed { get; private set; }

    public static void OnConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        IsCancelPressed = true;
        Console.WriteLine("Cancel key trapped...!");
    }
}

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
