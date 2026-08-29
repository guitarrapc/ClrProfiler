namespace ClrProfiler;

/// <summary>
/// Invokes user-supplied callbacks so their failures cannot damage the profiler itself.
/// </summary>
internal static class ProfilerCallbacks
{
    /// <summary>
    /// Reports an exception to the configured error callback. The callback is user code and may
    /// itself throw; that exception is swallowed, because the alternative is terminating a reader
    /// loop (silently stopping all delivery for that listener) or unwinding into the EventPipe
    /// dispatch thread or a timer thread (crashing the profiled process).
    /// </summary>
    public static void ReportError(Action<Exception>? onEventError, Exception exception)
    {
        try
        {
            onEventError?.Invoke(exception);
        }
        catch
        {
            // Nowhere safe to report a failure of the error reporter.
        }
    }
}
