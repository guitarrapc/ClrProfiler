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

// Run Server first
var host = "127.0.0.1";
var port = 8125;
using var cts = new CancellationTokenSource();
var server = new UdpServer(host, port)
{
    OnRecieveMessage = (_, text) =>
    {
        // echo client message on server.
        logger.LogInformation(text);
    }
};
var serverTask = Task.Run(async () => await server.ListenAsync(cts.Token), cts.Token);

// Enable Tracker
var useDatadog = true;
var tracker = useDatadog
    ? UseDatadogTracker(loggerFactory, host, port)
    : UseLoggerTracker(loggerFactory);

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

static ClrTracker UseDatadogTracker(ILoggerFactory loggerFactory, string host, int port)
{
    // Run Client (datadog agent with udp)
    var dogstatsdConfig = new StatsdConfig
    {
        StatsdServerName = host,
        StatsdPort = port,
        ConstantTags = [$"app:SandboxConsoleApp"],
    };
    DogStatsd.Configure(dogstatsdConfig);

    // enable clr tracker
    var tracker = new ClrTracker(loggerFactory);
    tracker.EnableTracker();
    tracker.StartTracker();
    return tracker;
}

static ClrTracker UseLoggerTracker(ILoggerFactory loggerFactory)
{
    // enable clr tracker
    var tracker = new ClrTracker(loggerFactory, new ClrTrackerOptions
    {
        TrackerType = ClrTrackerType.Logger
    });
    tracker.EnableTracker();
    tracker.StartTracker();
    return tracker;
}

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

public class UdpServer : IDisposable
{
    private System.Net.Sockets.UdpClient _udp;

    public Action<System.Net.IPEndPoint, string>? OnRecieveMessage { get; set; }

    public UdpServer(string host, int port)
    {
        var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(host), port);
        _udp = new System.Net.Sockets.UdpClient(endpoint);
    }

    public void Dispose()
    {
        _udp.Dispose();
    }

    public async ValueTask ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var result = await _udp.ReceiveAsync(ct);
            OnRecieveMessage?.Invoke(result.RemoteEndPoint, System.Text.Encoding.UTF8.GetString(result.Buffer));
        }
    }
}
