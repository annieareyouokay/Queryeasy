using System.Buffers.Binary;

namespace Queryeasy.Proxy.Tds;

internal sealed class TdsPacket
{
    public const int HeaderLength = 8;
    public const int MaxPacketLength = ushort.MaxValue;
    public const int MaxPayloadLength = MaxPacketLength - HeaderLength;

    public TdsPacket(
        byte type,
        byte status,
        ushort spid,
        byte packetId,
        byte window,
        byte[] payload)
    {
        if (payload.Length > MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "TDS payload is too large for a single packet.");
        }

        Type = type;
        Status = status;
        Spid = spid;
        PacketId = packetId;
        Window = window;
        Payload = payload;
    }

    public byte Type { get; }

    public byte Status { get; }

    public ushort Length => checked((ushort)(HeaderLength + Payload.Length));

    public ushort Spid { get; }

    public byte PacketId { get; }

    public byte Window { get; }

    public byte[] Payload { get; }

    public TdsPacketType? KnownType => Enum.IsDefined(typeof(TdsPacketType), Type)
        ? (TdsPacketType)Type
        : null;

    public bool IsEndOfMessage => ((TdsPacketStatus)Status).HasFlag(TdsPacketStatus.EndOfMessage);

    public byte[] ToArray()
    {
        var bytes = new byte[HeaderLength + Payload.Length];
        bytes[0] = Type;
        bytes[1] = Status;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2, 2), Length);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4, 2), Spid);
        bytes[6] = PacketId;
        bytes[7] = Window;
        Payload.CopyTo(bytes.AsSpan(HeaderLength));

        return bytes;
    }

    public TdsPacket WithPayload(byte[] payload, byte? status = null, byte? packetId = null)
    {
        return new TdsPacket(Type, status ?? Status, Spid, packetId ?? PacketId, Window, payload);
    }
}
