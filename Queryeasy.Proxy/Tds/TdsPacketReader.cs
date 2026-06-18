using System.Buffers.Binary;
using System.Net.Sockets;

namespace Queryeasy.Proxy.Tds;

internal sealed class TdsPacketReader
{
    private readonly NetworkStream _stream;

    public TdsPacketReader(NetworkStream stream)
    {
        _stream = stream;
    }

    public async Task<TdsPacket?> ReadAsync(CancellationToken cancellationToken)
    {
        var header = new byte[TdsPacket.HeaderLength];
        var bytesRead = await ReadExactOrEndAsync(header, cancellationToken);

        if (bytesRead == 0)
        {
            return null;
        }

        if (bytesRead != TdsPacket.HeaderLength)
        {
            throw new IOException($"Incomplete TDS header. Expected {TdsPacket.HeaderLength} bytes, got {bytesRead}.");
        }

        if (LooksLikeRawTlsRecord(header))
        {
            throw new RawTlsDetectedException(header);
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));

        if (length < TdsPacket.HeaderLength)
        {
            throw new IOException($"Invalid TDS packet length {length}.");
        }

        var payload = new byte[length - TdsPacket.HeaderLength];
        var payloadBytesRead = await ReadExactOrEndAsync(payload, cancellationToken);

        if (payloadBytesRead != payload.Length)
        {
            throw new IOException($"Incomplete TDS payload. Expected {payload.Length} bytes, got {payloadBytesRead}.");
        }

        return new TdsPacket(
            header[0],
            header[1],
            BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2)),
            header[6],
            header[7],
            payload);
    }

    private async Task<int> ReadExactOrEndAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var totalBytesRead = 0;

        while (totalBytesRead < buffer.Length)
        {
            var bytesRead = await _stream.ReadAsync(
                buffer.AsMemory(totalBytesRead, buffer.Length - totalBytesRead),
                cancellationToken);

            if (bytesRead == 0)
            {
                return totalBytesRead;
            }

            totalBytesRead += bytesRead;
        }

        return totalBytesRead;
    }

    private static bool LooksLikeRawTlsRecord(byte[] header)
    {
        var contentType = header[0];
        var majorVersion = header[1];
        var minorVersion = header[2];

        return contentType is 0x14 or 0x15 or 0x16 or 0x17
            && majorVersion == 0x03
            && minorVersion <= 0x04;
    }
}
