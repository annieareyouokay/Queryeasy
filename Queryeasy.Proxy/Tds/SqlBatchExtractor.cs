using System.Text;
using System.Buffers.Binary;

namespace Queryeasy.Proxy.Tds;

internal static class SqlBatchExtractor
{
    private static readonly Encoding SqlEncoding = Encoding.Unicode;

    public static SqlBatchMessage Decode(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return new SqlBatchMessage([], string.Empty);
        }

        var sqlOffset = GetSqlOffset(payload);
        var sqlLength = payload.Length - sqlOffset;
        var evenLength = sqlLength - sqlLength % 2;
        var headerPrefix = payload[..sqlOffset];
        var sql = SqlEncoding.GetString(payload, sqlOffset, evenLength);

        return new SqlBatchMessage(headerPrefix, sql);
    }

    public static byte[] Encode(SqlBatchMessage batch, string sql)
    {
        var sqlBytes = SqlEncoding.GetBytes(sql);
        var payload = new byte[batch.HeaderPrefix.Length + sqlBytes.Length];

        batch.HeaderPrefix.CopyTo(payload.AsSpan(0, batch.HeaderPrefix.Length));
        sqlBytes.CopyTo(payload.AsSpan(batch.HeaderPrefix.Length));

        return payload;
    }

    private static int GetSqlOffset(byte[] payload)
    {
        if (payload.Length < 4)
        {
            return 0;
        }

        var allHeadersLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));

        if (allHeadersLength < 4 || allHeadersLength > payload.Length)
        {
            return 0;
        }

        return (int)allHeadersLength;
    }
}
