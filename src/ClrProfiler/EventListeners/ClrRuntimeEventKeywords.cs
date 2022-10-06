namespace ClrProfiler.EventListeners;

// wrapper of ClrTraceEventParser.Keywords
// https://github.com/microsoft/perfview/blob/101984515958750f83063c117084eeec0866a19f/src/TraceEvent/Parsers/ClrTraceEventParser.cs#L36
[Flags]
public enum ClrRuntimeEventKeywords : long
{
    None = 0,
    /// <summary>
    /// Logging when garbage collections and finalization happen.
    /// </summary>
    GC = 0x1,
    /// <summary>
    /// Events when GC handles are set or destroyed.
    /// </summary>
    GCHandle = 0x2,
    /// <summary>
    /// Logging when modules actually get loaded and unloaded. 
    /// </summary>
    Loader = 0x8,
    /// <summary>
    /// Logging when Just in time (JIT) compilation occurs. 
    /// </summary>
    Jit = 0x10,
    /// <summary>
    /// Log when lock contention occurs.  (Monitor.Enters actually blocks)
    /// </summary>
    Contention = 0x4000,
    /// <summary>
    /// Log exception processing.
    /// </summary>
    Exceptions = 0x8000,
    /// <summary>
    /// Log events associated with the threadpool, and other threading events.  
    /// </summary>
    Threading = 0x10000,
    /// <summary>
    /// Recommend default flags (good compromise on verbosity).  
    /// </summary>
    Default = GC | Loader | Jit | Contention | Exceptions | Threading,
}
