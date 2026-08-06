using ClrProfiler.DatadogTracing;
using Microsoft.Extensions.Logging;

namespace CleProfiler.DatadogTracing.UnitTest;

public class LoggerMessageTest
{
    [Test]
    public async Task CallbackHandlers_LogExceptionWithStructuredMessage()
    {
        var logger = new CapturingLogger();
        var exception = new InvalidOperationException("callback {failed}");

        new DatadogTrackerCallbackHandler(logger).OnException(exception);
        new LoggerTrackerCallbackHandler(logger).OnException(exception);

        await Assert.That(logger.Entries).Count().IsEqualTo(2);
        foreach (var entry in logger.Entries)
        {
            await Assert.That(entry.Level).IsEqualTo(LogLevel.Critical);
            await Assert.That(entry.Exception).IsSameReferenceAs(exception);
            await Assert.That(entry.Message).IsEqualTo(exception.Message);
            await Assert.That(entry.Properties).Contains(new KeyValuePair<string, object?>("Message", exception.Message));
            await Assert.That(entry.Properties).Contains(new KeyValuePair<string, object?>("{OriginalFormat}", "{Message}"));
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IReadOnlyList<KeyValuePair<string, object?>> values
                ? values.ToArray()
                : [];
            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        Exception? Exception,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> Properties);
}
