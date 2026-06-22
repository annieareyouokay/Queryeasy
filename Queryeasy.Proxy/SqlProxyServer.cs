using System.Net;
using System.Net.Sockets;

namespace Queryeasy.Proxy;

internal sealed class SqlProxyServer
{
    private readonly ProxyOptions _options;
    private readonly ProxyMetrics _metrics;
    private readonly SemaphoreSlim _sessionSlots;

    public SqlProxyServer(ProxyOptions options, ProxyMetrics metrics)
    {
        _options = options;
        _metrics = metrics;
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
            await ObserveBackgroundTaskAsync(metricsSummary);
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
            await _sessionSlots.WaitAsync(cancellationToken);
            _metrics.SessionAccepted();
            return true;
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
            var session = new ProxySession(sessionId, client, _options, _metrics);
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
        }
    }

    private static async Task ObserveBackgroundTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
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
