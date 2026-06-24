using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Queryeasy.Proxy.Rewrite;
using Queryeasy.Proxy.Tds;
using Queryeasy.Proxy.Tds.PreLogin;

namespace Queryeasy.Proxy;

internal sealed class ProxySession
{
    private readonly string _sessionId;
    private readonly TcpClient _client;
    private readonly ProxyOptions _options;
    private readonly InspectionCapabilities _capabilities;
    private readonly SqlRewriter _rewriter;
    private readonly ProxyMetrics _metrics;
    private readonly SessionPerformanceTracker? _sessionPerformance;
    private readonly IPerformanceRecorder _performance;

    public ProxySession(
        string sessionId,
        TcpClient client,
        ProxyOptions options,
        InspectionCapabilities capabilities,
        SqlRewriter rewriter,
        ProxyMetrics metrics,
        SessionPerformanceTracker? sessionPerformance)
    {
        _sessionId = sessionId;
        _client = client;
        _options = options;
        _capabilities = capabilities;
        _rewriter = rewriter;
        _metrics = metrics;
        _sessionPerformance = sessionPerformance;
        _performance = (IPerformanceRecorder?)sessionPerformance ?? NoOpPerformanceRecorder.Instance;
    }

    public async Task RunAsync(CancellationToken serverCancellationToken)
    {
        try
        {
            using var target = new TcpClient { NoDelay = true };

            await ConnectToTargetAsync(target, serverCancellationToken);

            using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(serverCancellationToken);

            var clientStream = _client.GetStream();
            var targetStream = target.GetStream();
            var preLoginNegotiator = new TdsPreLoginNegotiator(
                _sessionId,
                clientStream,
                targetStream,
                _options,
                _performance);

            TdsPreLoginNegotiationResult preLoginResult;
            using (_performance.Measure(ProxyPerformanceStage.SessionPreLogin))
            {
                preLoginResult = await preLoginNegotiator.NegotiateAsync(sessionCancellation.Token);
            }

            var pendingS2cTraces = _sessionPerformance?.PendingS2cTraces;

            var clientToServerPipeline = new TdsClientToServerPipeline(
                _sessionId,
                clientStream,
                targetStream,
                _options,
                _capabilities,
                _rewriter,
                _metrics,
                _performance);

            var clientToServer = RunClientToServerAsync(clientToServerPipeline, sessionCancellation.Token);
            var serverToClient = RunServerToClientAsync(
                targetStream,
                clientStream,
                "sql -> client",
                sessionCancellation.Token,
                pendingS2cTraces);

            await Task.WhenAny(clientToServer, serverToClient);
            await sessionCancellation.CancelAsync();

            var clientToServerBytes = preLoginResult.ClientToServerBytes
                + await TaskObservation.IgnoreCancellationAsync(clientToServer);
            var serverToClientBytes = preLoginResult.ServerToClientBytes
                + await TaskObservation.IgnoreCancellationAsync(serverToClient);

            _metrics.AddClientToSqlBytes(clientToServerBytes);
            _metrics.AddSqlToClientBytes(serverToClientBytes);

            ProxyLog.Info(
                $"[{_sessionId}] Session closed. Sent {clientToServerBytes} bytes to SQL Server, received {serverToClientBytes} bytes.");
        }
        finally
        {
            CompleteSessionPerformance();
        }
    }

    private void CompleteSessionPerformance()
    {
        if (_sessionPerformance is null)
        {
            return;
        }

        var summary = _sessionPerformance.BuildSessionSummary();
        _sessionPerformance.Complete();

        if (_options.LogLevel >= ProxyLogLevel.Debug && !string.IsNullOrEmpty(summary))
        {
            ProxyLog.Debug($"[{_sessionId}] {summary}");
        }
    }

    private async Task ConnectToTargetAsync(TcpClient target, CancellationToken serverCancellationToken)
    {
        using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(serverCancellationToken);
        connectCancellation.CancelAfter(_options.ConnectTimeout);

        ProxyLog.Info($"[{_sessionId}] Connecting to SQL Server {_options.TargetHost}:{_options.TargetPort}.");

        using (_performance.Measure(ProxyPerformanceStage.SessionConnect))
        {
            await target.ConnectAsync(_options.TargetHost, _options.TargetPort, connectCancellation.Token);
        }

        ProxyLog.Info($"[{_sessionId}] Connected to SQL Server.");
    }

    private async Task<long> RunClientToServerAsync(
        TdsClientToServerPipeline pipeline,
        CancellationToken cancellationToken)
    {
        using (_performance.Measure(ProxyPerformanceStage.SessionClientToServer))
        {
            return await pipeline.RunAsync(cancellationToken);
        }
    }

    private Task<long> RunServerToClientAsync(
        NetworkStream source,
        NetworkStream destination,
        string direction,
        CancellationToken cancellationToken,
        ConcurrentQueue<RequestTrace>? pendingS2cTraces)
    {
        return MeasureServerToClientAsync(source, destination, direction, cancellationToken, pendingS2cTraces);
    }

    private async Task<long> MeasureServerToClientAsync(
        NetworkStream source,
        NetworkStream destination,
        string direction,
        CancellationToken cancellationToken,
        ConcurrentQueue<RequestTrace>? pendingS2cTraces)
    {
        using (_performance.Measure(ProxyPerformanceStage.SessionServerToClient))
        {
            return await CopyUntilClosedAsync(source, destination, direction, cancellationToken, pendingS2cTraces);
        }
    }

    private async Task<long> CopyUntilClosedAsync(
        NetworkStream source,
        NetworkStream destination,
        string direction,
        CancellationToken cancellationToken,
        ConcurrentQueue<RequestTrace>? pendingS2cTraces)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_options.BufferSizeBytes);
        var totalBytes = 0L;
        RequestTrace? currentS2cTrace = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Before reading, try to dequeue a pending c2s trace to correlate with s2c
                if (currentS2cTrace is null && pendingS2cTraces?.TryDequeue(out var trace) == true)
                {
                    currentS2cTrace = trace;
                    currentS2cTrace.S2cReadStartTimestamp = Stopwatch.GetTimestamp();
                }

                int bytesRead;
                using (_performance.Measure(ProxyPerformanceStage.S2cRead))
                {
                    bytesRead = await ReadWithIdleTimeoutAsync(source, buffer, cancellationToken);
                }

                if (bytesRead == 0)
                {
                    ProxyLog.Debug($"[{_sessionId}] {direction} closed by peer.");
                    break;
                }

                using (_performance.Measure(ProxyPerformanceStage.S2cWrite))
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }

                // After write, complete the s2c trace if we have one
                if (currentS2cTrace is not null)
                {
                    var writeTimestamp = Stopwatch.GetTimestamp();
                    var readStart = currentS2cTrace.S2cReadStartTimestamp;
                    if (_sessionPerformance is not null)
                    {
                        _sessionPerformance.CompleteS2cTrace(currentS2cTrace, readStart, writeTimestamp);

                        // Log per-request waterfall at Trace level
                        if (_options.LogLevel >= ProxyLogLevel.Trace)
                        {
                            ProxyLog.Trace($"[{_sessionId}] {currentS2cTrace.BuildWaterfall()}");
                        }
                    }

                    currentS2cTrace = null;
                }

                totalBytes += bytesRead;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            ProxyLog.Warn($"[{_sessionId}] {direction} stopped after idle timeout.");
        }
        catch (IOException ex)
        {
            ProxyLog.Debug($"[{_sessionId}] {direction} I/O ended: {ex.Message}");
        }
        catch (SocketException ex)
        {
            ProxyLog.Debug($"[{_sessionId}] {direction} socket ended: {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return totalBytes;
    }

    private async Task<int> ReadWithIdleTimeoutAsync(
        NetworkStream source,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        using var idleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idleCancellation.CancelAfter(_options.IdleTimeout);

        try
        {
            return await source.ReadAsync(buffer.AsMemory(0, buffer.Length), idleCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ProxyLog.Warn($"[{_sessionId}] Session idle timeout reached.");
            throw;
        }
    }
}
