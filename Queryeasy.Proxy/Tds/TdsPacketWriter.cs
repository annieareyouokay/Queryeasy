using System.Net.Sockets;

namespace Queryeasy.Proxy.Tds;

internal sealed class TdsPacketWriter
{
    private readonly Stream _stream;

    public TdsPacketWriter(Stream stream)
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
        var packetList = packets as IReadOnlyList<TdsPacket> ?? packets.ToArray();

        if (packetList.Count == 0)
        {
            return;
        }

        if (packetList.Count == 1)
        {
            await WriteAsync(packetList[0], cancellationToken);
            return;
        }

        foreach (var packet in packetList)
        {
            await _stream.WriteAsync(packet.ToArray(), cancellationToken);
        }

        await _stream.FlushAsync(cancellationToken);
    }
}
