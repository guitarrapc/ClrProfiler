using StatsdClient;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TUnit.Core.Interfaces;

namespace CleProfiler.DatadogTracing.UnitTest;

/// <summary>
/// Owns the one DogStatsD configuration and the one listening socket for the whole test session,
/// so wire-level tests can assert the statsd lines the adapters actually emit.
/// </summary>
/// <remarks>
/// <see cref="DogStatsd.Configure"/> writes process-global static state. Calling it a second time
/// in one process leaves metrics from the earlier configuration undeliverable, so a test waiting
/// on them never completes. Wire-level tests therefore take this fixture instead of configuring
/// DogStatsD themselves, and serialize on <see cref="SerializationKey"/> so their captures do not
/// interleave. The socket binds port 0 and reports the assigned port, so a leaked process from an
/// earlier run cannot make the next run fail to bind.
/// </remarks>
public sealed class DogStatsdWireFixture : IAsyncInitializer, IAsyncDisposable
{
    /// <summary>Apply as <c>[NotInParallel(DogStatsdWireFixture.SerializationKey)]</c> on every test class using this fixture.</summary>
    public const string SerializationKey = "DogStatsdWire";

    /// <summary>Constant tag attached to every metric this fixture receives.</summary>
    public const string ConstantTag = "app:ClrProfiler.DatadogTracing.UnitTest";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    private readonly List<string> _lines = [];
    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _udp;
    private Task? _listener;

    /// <summary>Dynamically assigned port the configured DogStatsD client sends to.</summary>
    public int Port { get; private set; }

    public Task InitializeAsync()
    {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
        _listener = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        DogStatsd.Configure(new StatsdConfig
        {
            StatsdServerName = IPAddress.Loopback.ToString(),
            StatsdPort = Port,
            ConstantTags = [ConstantTag],
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts observing lines received from this point on, so a test never sees what an earlier
    /// test produced on the shared socket.
    /// </summary>
    public WireCapture StartCapture()
    {
        lock (_lines)
        {
            return new WireCapture(this, _lines.Count);
        }
    }

    internal string[] LinesFrom(int offset)
    {
        lock (_lines)
        {
            return offset >= _lines.Count ? [] : [.. _lines.GetRange(offset, _lines.Count - offset)];
        }
    }

    internal static TimeSpan Poll => PollInterval;

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _udp!.ReceiveAsync(cancellationToken);
                var text = Encoding.UTF8.GetString(result.Buffer);
                // One datagram can carry several metrics, so split rather than treating the
                // payload as a single line.
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    // The client reports its own delivery telemetry over the same socket.
                    if (line.StartsWith("datadog.dogstatsd", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    lock (_lines)
                    {
                        _lines.Add(line);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _udp?.Dispose();
        if (_listener is not null)
        {
            try
            {
                await _listener;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _cts.Dispose();
    }
}

/// <summary>Lines received on the shared socket since the capture was started.</summary>
public sealed class WireCapture(DogStatsdWireFixture fixture, int offset)
{
    /// <summary>Snapshot of the statsd lines received so far.</summary>
    public string[] Lines => fixture.LinesFrom(offset);

    /// <summary>
    /// Waits until every entry in <paramref name="requiredSubstrings"/> appears in some received
    /// line, then returns the lines. Throws <see cref="TimeoutException"/> naming the substrings
    /// that never arrived rather than blocking forever.
    /// </summary>
    /// <param name="produce">
    /// Optional work to repeat while waiting, for metrics that need runtime activity to occur.
    /// </param>
    public async Task<string[]> WaitForAllAsync(
        IReadOnlyList<string> requiredSubstrings,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<Task>? produce = null)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (true)
        {
            var lines = Lines;
            var missing = requiredSubstrings
                .Where(required => !Array.Exists(lines, line => line.Contains(required, StringComparison.Ordinal)))
                .ToArray();
            if (missing.Length == 0)
            {
                return lines;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Timed out after {timeout.TotalSeconds:N0}s waiting for {missing.Length} statsd metric(s):{Environment.NewLine}" +
                    $"  missing: {string.Join(Environment.NewLine + "           ", missing)}{Environment.NewLine}" +
                    $"  received {lines.Length} line(s):{Environment.NewLine}" +
                    (lines.Length == 0 ? "    (none)" : "    " + string.Join(Environment.NewLine + "    ", lines)));
            }

            if (produce is not null)
            {
                await produce();
            }
            await Task.Delay(DogStatsdWireFixture.Poll, cancellationToken);
        }
    }
}
