using System.Net;
using System.Net.Sockets;
using Queryeasy.Proxy.Rewrite;

namespace Queryeasy.Proxy;

internal sealed class SqlProxyServer
{
    private readonly ProxyOptions _options;
    private readonly ProxyMetrics _metrics;
    private readonly ProxyPerformanceMetrics _performanceMetrics;
    private readonly InspectionCapabilities _capabilities;
    private readonly SqlRewriter _rewriter;
    private readonly SemaphoreSlim _sessionSlots;

    public SqlProxyServer(ProxyOptions options, ProxyMetrics metrics, ProxyPerformanceMetrics performanceMetrics)
    {
        _options = options;
        _metrics = metrics;
        _performanceMetrics = performanceMetrics;
        _capabilities = options.GetInspectionCapabilities();
        _rewriter = new SqlRewriter(options.RewriteRules);
        _sessionSlots = new SemaphoreSlim(options.MaxConcurrentSessions, options.MaxConcurrentSessions);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var listenAddress = await ResolveListenAddressAsync(_options.ListenHost, cancellationToken);
        var listener = new TcpListener(listenAddress, _options.ListenPort);

        listener.Start();

        ProxyLog.Info(
            $"MSSQL proxy listening on {listenAddress}:{_options.ListenPort}, forwarding to {_options.TargetHost}:{_options.TargetPort}.");
        ProxyLog.Info("Press Ctrl+C to stop.");
        using var metricsCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var metricsSummary = RunMetricsSummaryAsync(metricsCancellation.Token);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                client.NoDelay = true;

                if (!await TryAcquireSessionSlotAsync(client, cancellationToken))
                {
                    continue;
                }

                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
            await metricsCancellation.CancelAsync();
            await TaskObservation.IgnoreCancellationAsync(metricsSummary);
            await ProxyLog.ShutdownAsync();
        }
    }

    private async Task<bool> TryAcquireSessionSlotAsync(TcpClient client, CancellationToken cancellationToken)
    {
        if (_sessionSlots.Wait(0))
        {
            _metrics.SessionAccepted();
            return true;
        }

        if (!_options.RejectWhenOverloaded)
        {
            try
            {
                await _sessionSlots.WaitAsync(cancellationToken);
                _metrics.SessionAccepted();
                return true;
            }
            catch (OperationCanceledException)
            {
                client.Dispose();
                throw;
            }
        }

        _metrics.SessionRejected();
        ProxyLog.Warn("Rejecting client connection because MaxConcurrentSessions was reached.");
        client.Dispose();
        return false;
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCancellationToken)
    {
        using var clientGuard = client;
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

        ProxyLog.Info($"[{sessionId}] Client connected from {remoteEndpoint}.");

        try
        {
            var sessionPerformance = _options.EnablePerformanceMetrics
                ? _performanceMetrics.CreateSessionTracker(_options.PerformanceTraceBufferCapacity)
                : null;
            var session = new ProxySession(
                sessionId,
                client,
                _options,
                _capabilities,
                _rewriter,
                _metrics,
                sessionPerformance);
            await session.RunAsync(serverCancellationToken);
        }
        catch (OperationCanceledException) when (serverCancellationToken.IsCancellationRequested)
        {
            ProxyLog.Info($"[{sessionId}] Session stopped by server shutdown.");
        }
        catch (Exception ex)
        {
            ProxyLog.Error($"[{sessionId}] Session failed: {ex.Message}");
        }
        finally
        {
            _metrics.SessionClosed();
            _sessionSlots.Release();
            ProxyLog.Info($"[{sessionId}] Client disconnected.");
        }
    }

    private async Task RunMetricsSummaryAsync(CancellationToken cancellationToken)
    {
        if (_options.MetricsSummaryIntervalSeconds == 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(_options.MetricsSummaryInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            ProxyLog.Info(_metrics.BuildSummary());

            if (_options.EnablePerformanceMetrics)
            {
                ProxyLog.Info(_performanceMetrics.BuildHumanSummary());

                var json = _performanceMetrics.BuildJsonSummary();

                if (_options.LogLevel >= ProxyLogLevel.Debug)
                {
                    ProxyLog.Debug(json);
                }

                if (!string.IsNullOrEmpty(_options.PerformanceJsonLogPath))
                {
                    await AppendJsonToFileAsync(json, _options.PerformanceJsonLogPath, cancellationToken);
                }

                _performanceMetrics.ResetAll();
            }
        }
    }

    private static async Task AppendJsonToFileAsync(string json, string path, CancellationToken cancellationToken)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.AppendAllTextAsync(path, json + Environment.NewLine, cancellationToken);
        }
        catch (Exception ex)
        {
            ProxyLog.Warn($"Failed to write performance JSON to '{path}': {ex.Message}");
        }
    }

    private static async Task<IPAddress> ResolveListenAddressAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return address;
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);

        return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException($"Could not resolve listen host '{host}'.");
    }
}
