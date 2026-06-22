namespace Queryeasy.Proxy;

internal static class ProxyLog
{
    private static readonly object Sync = new();
    private static ProxyLogLevel _minimumLevel = ProxyLogLevel.Info;

    public static void Configure(ProxyLogLevel minimumLevel)
    {
        _minimumLevel = minimumLevel;
    }

    public static void Error(string message)
    {
        Write(ProxyLogLevel.Error, message);
    }

    public static void Warn(string message)
    {
        Write(ProxyLogLevel.Warn, message);
    }

    public static void Info(string message)
    {
        Write(ProxyLogLevel.Info, message);
    }

    public static void Debug(string message)
    {
        Write(ProxyLogLevel.Debug, message);
    }

    public static void Trace(string message)
    {
        Write(ProxyLogLevel.Trace, message);
    }

    public static bool IsEnabled(ProxyLogLevel level)
    {
        return level <= _minimumLevel;
    }

    private static void Write(ProxyLogLevel level, string message)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        lock (Sync)
        {
            Console.WriteLine($"{DateTimeOffset.UtcNow:O} [{level}] {message}");
        }
    }
}
