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

    public ProxySession(string sessionId, TcpClient client, ProxyOptions options)
    {
        _sessionId = sessionId;
        _client = client;
        _options = options;
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
            _options);

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

        Console.WriteLine(
            $"[{_sessionId}] Session closed. Sent {clientToServerBytes} bytes to SQL Server, received {serverToClientBytes} bytes.");
    }

    private async Task ConnectToTargetAsync(TcpClient target, CancellationToken serverCancellationToken)
    {
        using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(serverCancellationToken);
        connectCancellation.CancelAfter(_options.ConnectTimeout);

        Console.WriteLine($"[{_sessionId}] Connecting to SQL Server {_options.TargetHost}:{_options.TargetPort}.");

        await target.ConnectAsync(_options.TargetHost, _options.TargetPort, connectCancellation.Token);

        Console.WriteLine($"[{_sessionId}] Connected to SQL Server.");
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
                    Console.WriteLine($"[{_sessionId}] {direction} closed by peer.");
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
            Console.WriteLine($"[{_sessionId}] {direction} stopped after idle timeout.");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[{_sessionId}] {direction} I/O ended: {ex.Message}");
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"[{_sessionId}] {direction} socket ended: {ex.Message}");
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
            Console.WriteLine($"[{_sessionId}] Session idle timeout reached.");
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
