using System.Buffers;
using System.Net.Sockets;
using Queryeasy.Proxy.Tds;
using Queryeasy.Proxy.Tds.PreLogin;

namespace Queryeasy.Proxy;

internal sealed class ProxySession
{
    private readonly string _sessionId;
    private readonly TcpClient _client;
    private readonly ProxyOptions _options;
    private readonly ProxyMetrics _metrics;

    public ProxySession(string sessionId, TcpClient client, ProxyOptions options, ProxyMetrics metrics)
    {
        _sessionId = sessionId;
        _client = client;
        _options = options;
        _metrics = metrics;
    }

    public async Task RunAsync(CancellationToken serverCancellationToken)
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
            _options);

        var preLoginResult = await preLoginNegotiator.NegotiateAsync(sessionCancellation.Token);

        var clientToServerPipeline = new TdsClientToServerPipeline(
            _sessionId,
            clientStream,
            targetStream,
            _options,
            _metrics);

        var clientToServer = clientToServerPipeline.RunAsync(sessionCancellation.Token);

        var serverToClient = CopyUntilClosedAsync(
            targetStream,
            clientStream,
            "sql -> client",
            sessionCancellation.Token);

        await Task.WhenAny(clientToServer, serverToClient);
        await sessionCancellation.CancelAsync();

        var clientToServerBytes = preLoginResult.ClientToServerBytes + await ObserveCopyResultAsync(clientToServer);
        var serverToClientBytes = preLoginResult.ServerToClientBytes + await ObserveCopyResultAsync(serverToClient);

        _metrics.AddClientToSqlBytes(clientToServerBytes);
        _metrics.AddSqlToClientBytes(serverToClientBytes);

        ProxyLog.Info(
            $"[{_sessionId}] Session closed. Sent {clientToServerBytes} bytes to SQL Server, received {serverToClientBytes} bytes.");
    }

    private async Task ConnectToTargetAsync(TcpClient target, CancellationToken serverCancellationToken)
    {
        using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(serverCancellationToken);
        connectCancellation.CancelAfter(_options.ConnectTimeout);

        ProxyLog.Info($"[{_sessionId}] Connecting to SQL Server {_options.TargetHost}:{_options.TargetPort}.");

        await target.ConnectAsync(_options.TargetHost, _options.TargetPort, connectCancellation.Token);

        ProxyLog.Info($"[{_sessionId}] Connected to SQL Server.");
    }

    private async Task<long> CopyUntilClosedAsync(
        NetworkStream source,
        NetworkStream destination,
        string direction,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_options.BufferSizeBytes);
        var totalBytes = 0L;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await ReadWithIdleTimeoutAsync(source, buffer, cancellationToken);

                if (bytesRead == 0)
                {
                    ProxyLog.Debug($"[{_sessionId}] {direction} closed by peer.");
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                await destination.FlushAsync(cancellationToken);

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

    private static async Task<long> ObserveCopyResultAsync(Task<long> copyTask)
    {
        try
        {
            return await copyTask;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
    }
}
