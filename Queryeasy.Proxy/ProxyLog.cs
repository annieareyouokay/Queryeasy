using System.Threading.Channels;

namespace Queryeasy.Proxy;

internal static class ProxyLog
{
    private static readonly object Sync = new();
    private static ProxyLogLevel _minimumLevel = ProxyLogLevel.Info;
    private static bool _asyncLoggingEnabled;
    private static Channel<string>? _channel;
    private static Task? _writerTask;
    private static CancellationTokenSource? _writerCancellation;

    public static void Configure(ProxyLogLevel minimumLevel, bool asyncLogging = true)
    {
        _minimumLevel = minimumLevel;
        _asyncLoggingEnabled = asyncLogging;

        if (_asyncLoggingEnabled)
        {
            StartBackgroundWriter();
        }
    }

    public static async Task ShutdownAsync()
    {
        Channel<string>? channel;
        Task? writerTask;
        CancellationTokenSource? writerCancellation;

        lock (Sync)
        {
            channel = _channel;
            writerTask = _writerTask;
            writerCancellation = _writerCancellation;
            _channel = null;
            _writerTask = null;
            _writerCancellation = null;
        }

        if (channel is null)
        {
            return;
        }

        channel.Writer.TryComplete();
        writerCancellation?.Cancel();

        if (writerTask is not null)
        {
            try
            {
                await writerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        writerCancellation?.Dispose();
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

        var formatted = $"{DateTimeOffset.UtcNow:O} [{level}] {message}";

        if (_asyncLoggingEnabled && _channel is not null)
        {
            _channel.Writer.TryWrite(formatted);
            return;
        }

        lock (Sync)
        {
            Console.WriteLine(formatted);
        }
    }

    private static void StartBackgroundWriter()
    {
        lock (Sync)
        {
            if (_writerTask is not null)
            {
                return;
            }

            _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _writerCancellation = new CancellationTokenSource();
            _writerTask = RunBackgroundWriterAsync(_channel.Reader, _writerCancellation.Token);
        }
    }

    private static async Task RunBackgroundWriterAsync(ChannelReader<string> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                Console.WriteLine(message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
