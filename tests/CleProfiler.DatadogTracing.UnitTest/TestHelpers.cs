using Microsoft.Extensions.Logging;

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
}
