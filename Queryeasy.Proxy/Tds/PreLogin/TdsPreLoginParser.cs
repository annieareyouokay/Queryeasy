using System.Buffers.Binary;

namespace Queryeasy.Proxy.Tds.PreLogin;

internal static class TdsPreLoginParser
{
    private const int OptionEntryLength = 5;

    public static TdsPreLoginMessage Parse(byte[] payload)
    {
        var options = new List<TdsPreLoginOptionEntry>();
        var offset = 0;

        while (offset < payload.Length)
        {
            var token = payload[offset];

            if (token == (byte)TdsPreLoginOption.Terminator)
            {
                return new TdsPreLoginMessage(payload, options);
            }

            if (offset + OptionEntryLength > payload.Length)
            {
                throw new IOException("Invalid TDS PreLogin option table.");
            }

            var dataOffset = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset + 1, 2));
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset + 3, 2));

            if (dataOffset + dataLength > payload.Length)
            {
                throw new IOException(
                    $"Invalid TDS PreLogin option token 0x{token:X2}: offset={dataOffset}, length={dataLength}.");
            }

            options.Add(new TdsPreLoginOptionEntry(token, dataOffset, dataLength));
            offset += OptionEntryLength;
        }

        throw new IOException("TDS PreLogin terminator was not found.");
    }
}
