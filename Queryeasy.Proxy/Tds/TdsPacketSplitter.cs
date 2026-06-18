namespace Queryeasy.Proxy.Tds;

internal static class TdsPacketSplitter
{
    public static IReadOnlyList<TdsPacket> SplitLike(TdsPacket template, byte[] payload, int maxPayloadLength)
    {
        var safeMaxPayloadLength = Math.Clamp(maxPayloadLength, 1, TdsPacket.MaxPayloadLength);
        var packets = new List<TdsPacket>();
        var offset = 0;
        var packetId = template.PacketId;
        var statusWithoutEndOfMessage = (byte)(template.Status & ~(byte)TdsPacketStatus.EndOfMessage);

        if (payload.Length == 0)
        {
            packets.Add(template.WithPayload([], (byte)(statusWithoutEndOfMessage | (byte)TdsPacketStatus.EndOfMessage), packetId));
            return packets;
        }

        while (offset < payload.Length)
        {
            var count = Math.Min(safeMaxPayloadLength, payload.Length - offset);
            var chunk = new byte[count];
            Array.Copy(payload, offset, chunk, 0, count);

            offset += count;
            var isLast = offset >= payload.Length;
            var status = isLast
                ? (byte)(statusWithoutEndOfMessage | (byte)TdsPacketStatus.EndOfMessage)
                : statusWithoutEndOfMessage;

            packets.Add(template.WithPayload(chunk, status, packetId));
            packetId++;
        }

        return packets;
    }
}
