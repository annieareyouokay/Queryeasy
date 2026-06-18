using System.Net;
using System.Net.Sockets;

namespace Queryeasy.Proxy;

internal sealed class SqlProxyServer
{
    private readonly ProxyOptions _options;

    public SqlProxyServer(ProxyOptions options)
    {
        _options = options;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var listenAddress = await ResolveListenAddressAsync(_options.ListenHost, cancellationToken);
        var listener = new TcpListener(listenAddress, _options.ListenPort);

        listener.Start();

        Console.WriteLine(
            $"MSSQL proxy listening on {listenAddress}:{_options.ListenPort}, forwarding to {_options.TargetHost}:{_options.TargetPort}.");
        Console.WriteLine("Press Ctrl+C to stop.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                client.NoDelay = true;

                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCancellationToken)
    {
        using var clientGuard = client;
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

        Console.WriteLine($"[{sessionId}] Client connected from {remoteEndpoint}.");

        try
        {
            var session = new ProxySession(sessionId, client, _options);
            await session.RunAsync(serverCancellationToken);
        }
        catch (OperationCanceledException) when (serverCancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"[{sessionId}] Session stopped by server shutdown.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{sessionId}] Session failed: {ex.Message}");
        }
        finally
        {
            Console.WriteLine($"[{sessionId}] Client disconnected.");
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
