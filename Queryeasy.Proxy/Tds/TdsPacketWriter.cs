using System.Net.Sockets;

namespace Queryeasy.Proxy.Tds;

internal sealed class TdsPacketWriter
{
    private readonly NetworkStream _stream;

    public TdsPacketWriter(NetworkStream stream)
    {
        _stream = stream;
    }

    public async Task WriteAsync(TdsPacket packet, CancellationToken cancellationToken)
    {
        var bytes = packet.ToArray();
        await _stream.WriteAsync(bytes, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    public async Task WriteAsync(IEnumerable<TdsPacket> packets, CancellationToken cancellationToken)
    {
        foreach (var packet in packets)
        {
            await WriteAsync(packet, cancellationToken);
        }
    }
}
