using ClrProfiler.DatadogTracing;
using Microsoft.Extensions.Logging;
using StatsdClient;
using ZLogger;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.ClearProviders();
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddZLoggerConsole();
});
var logger = loggerFactory.CreateLogger<Program>();

// enable datadog (use udp port)
var dogstatsdConfig = new StatsdConfig
{
    StatsdServerName = "localhost",
    StatsdPort = 8125,
    ConstantTags = new[] { $"app:SandboxConsoleApp" },
};
DogStatsd.Configure(dogstatsdConfig);

// enable clr tracker
var tracker = new ClrTracker(logger);
tracker.StartTracker();

Console.CancelKeyPress += ConsoleHelper.OnConsoleCancelKeyPress;

logger.LogInformation("Press Ctrl+C to cancel execution.");
while (!ConsoleHelper.IsCancelPressed)
{
    Allocate10();
    Allocate5K();
    GC.Collect();
    await Task.Delay(100);
}

tracker.StopTracker();
tracker.CancelTracker();

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
