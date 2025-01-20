using ClrProfiler.DatadogTracing;
using FluentAssertions;
using StatsdClient;

namespace CleProfiler.DatadogTracing.UnitTest;

public class DatadogTracingUnitTest
{
    [Fact]
    public async Task DatadogTracingGCUniTest()
    {
        using var cts = new CancellationTokenSource();
        var logger = TestHelpers.CreateLogger<DatadogTracingUnitTest>();
        var host = "127.0.0.1";
        var port = 8125;
        var tag = "app:CleProfiler.DatadogTracing.UnitTest";
        var complete = false;
        var list = new List<string>();

        // server
        var server = new TestHelpers.UdpServer(host, port)
        {
            OnRecieveMessage = (_, text) =>
            {
                if (!text.StartsWith("datadog.dogstatsd"))
                {
                    list.Add(text);
                }
            }
        };
        var serverTask = Task.Run(async () => await server.ListenAsync(cts.Token), cts.Token);

        // client
        var dogstatsdConfig = new StatsdConfig
        {
            StatsdServerName = host,
            StatsdPort = port,
            ConstantTags = new[] { tag },
        };
        DogStatsd.Configure(dogstatsdConfig);

        // enable clr tracker
        using var loggerFactory = TestHelpers.CreateLoggerFactory();
        var tracker = new ClrTracker(loggerFactory);
        tracker.StartTracker();

        // Allocate and GC
        while (!complete)
        {
            TestHelpers.Allocate5K();
            GC.Collect();
            await Task.Delay(10);

            if (list.Count >= 20)
            {
                complete = true;
            }
        }

        //Assert.Equal("clr_diagnostics_event.gc.startend_count:18|c|#app:SandboxConsoleApp,gc_gen:2,gc_type:0,gc_reason:induced\nclr_diagnostics_event.gc.suspend_object_count:181|c|#app:SandboxConsoleApp,gc_suspend_reason:gc\n", output);
        list.Should().AllSatisfy(x => x.Contains(tag));
        list.Where(x => x.Contains("clr_diagnostics_event.gc.suspend_object_count")).Should().NotBeEmpty();
        list.Where(x => x.Contains("clr_diagnostics_event.gc.suspend_duration_ms")).Should().NotBeEmpty();

        tracker.StopTracker();
        cts.Cancel();
    }
}
