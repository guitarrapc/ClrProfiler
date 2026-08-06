using Microsoft.Extensions.Logging;

namespace ClrProfiler.DatadogTracing;

internal static partial class LogMessages
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Enable ClrTracker")]
    internal static partial void EnableTracker(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Start tracking ClrTracker")]
    internal static partial void StartTracker(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Stop tracking ClrTracker")]
    internal static partial void StopTracker(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Restart tracking ClrTracker")]
    internal static partial void RestartTracker(ILogger logger);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "Cancel tracking ClrTracker")]
    internal static partial void CancelTracker(ILogger logger);

    [LoggerMessage(EventId = 6, Level = LogLevel.Critical, Message = "{Message}")]
    internal static partial void CallbackException(ILogger logger, Exception exception, string message);
}
