using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CleProfiler.DatadogTracing.UnitTest;

public static class TestHelpers
{
    public static ILoggerFactory CreateLoggerFactory()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        return loggerFactory;
    }
    public static ILogger<T> CreateLogger<T>()
    {
        using var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<T>();
        return logger;
    }

    public static void Allocate10()
    {
        for (int i = 0; i < 10; i++)
        {
            int[] x = new int[100];
        }
    }

    public static void Allocate5K()
    {
        for (int i = 0; i < 5000; i++)
        {
            int[] x = new int[100];
        }
    }

    public class UdpServer : IDisposable
    {
        private UdpClient _udp;

        public Action<IPEndPoint, string>? OnRecieveMessage { get; set; }

        public UdpServer(string host, int port)
        {
            var endpoint = new System.Net.IPEndPoint(IPAddress.Parse(host), port);
            _udp = new UdpClient(endpoint);
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
                OnRecieveMessage?.Invoke(result.RemoteEndPoint, Encoding.UTF8.GetString(result.Buffer));
            }
        }
    }
}
